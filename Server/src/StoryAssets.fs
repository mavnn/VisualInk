module VisualInk.Server.StoryAssets

open Prelude
open Falco
open System.IO
open Falco.Routing
open Falco.Markup
open Microsoft.AspNetCore.Hosting
open Falco.Htmx
open View
open Falco.Security
open System.Text.Json.Serialization
open System.Linq
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Hosting

module B = Bulma

let private urlEncode (str: string) =
  System.Web.HttpUtility.UrlEncode str

[<JsonFSharpConverter(BaseUnionEncoding = JsonUnionEncoding.UnwrapFieldlessTags)>]
type AssetCategory =
  | SpeakerAsset
  | SceneAsset
  | MusicAsset

[<JsonFSharpConverter(BaseUnionEncoding = JsonUnionEncoding.UnwrapFieldlessTags)>]
type AssetOwner =
  | Shared
  | UserOwned

[<JsonFSharpConverter>]
type SubAssetMetadata = { name: string; url: string }

[<JsonFSharpConverter>]
type AssetMetadata =
  { [<Marten.Schema.Identity>]
    name: string
    owner: AssetOwner
    category: AssetCategory
    url: string
    subAssets: SubAssetMetadata list }


let private assetGrid cells =
  B.block
    []
    [ B.fixedGrid
        [ _class_
            "has-6-cols-widescreen has-4-cols-tablet has-1-cols-mobile" ]
        cells ]

module private Queries =
  open Marten
  open Marten.Linq.MatchesSql
  open JasperFx

  let assetsByCategory (category: AssetCategory) =
    handler {
      let! maybeUser = User.getSessionUser ()

      return!
        match maybeUser with
        | Some user ->
          DocStore.query<AssetMetadata> (fun q ->
            q.Where(fun a ->
              a.MatchesSql(
                "data ->> 'category' = ?",
                sprintf "%A" category
              )
              && a.TenantIsOneOf(
                user.id.ToString(),
                StorageConstants.DefaultTenantId
              )))
        | None ->
          DocStore.queryShared<AssetMetadata, _> (fun q ->
            q.Where(fun a ->
              a.MatchesSql(
                "data ->> 'category' = ?",
                sprintf "%A" category
              )))
    }

  let assetByNameAs
    (tenantId: System.Guid option)
    (name: string)
    (category: AssetCategory)
    =
    handler {
      let! logger =
        Handler.plug<ILogger<AssetMetadata>, _> ()

      let tenantStr =
        match tenantId with
        | Some tid -> tid.ToString()
        | None -> JasperFx.MultiTenancy.TenantId.DefaultTenantId

      logger.LogDebug(
        "Querying owner tenant for {category} named {name}",
        category,
        name
      )

      let! asset =
        DocStore.singleShared<AssetMetadata, _> (fun q ->
          q
            .Where(fun a -> a.TenantIsOneOf tenantStr)
            .Where(fun a -> a.name = name)
            .Where(fun a ->
              a.MatchesSql(
                "data ->> 'category' = ?",
                sprintf "%A" category
              )))

      return!
        match asset with
        | Some _ -> Handler.return' asset
        | None ->
          logger.LogDebug(
            "Querying default tenant for {category} named {name}",
            category,
            name
          )

          DocStore.singleShared<AssetMetadata, _> (fun q ->
            q
              .Where(fun a -> a.name = name)
              .Where(fun a ->
                a.MatchesSql(
                  "data ->> 'category' = ?",
                  sprintf "%A" category
                )))
    }

  let assetByName name category =
    User.getSessionUser ()
    |> Handler.mapOption (fun user -> user.id)
    |> Handler.bind (fun user ->
      assetByNameAs user name category)

  // Call this when a url is removed from the assets metadata
  // store so we can check if the file pointed at can be deleted.
  let getRefCount url =
    handler {
      use! session = DocStore.startSharedSession ()

      return!
        session
          .Query<AssetMetadata>()
          .Where(fun x -> x.AnyTenant())
          .Where(fun x ->
            x.url = url
            || x.MatchesSql(
              "exists (select true from jsonb_array_elements(data -> 'subAssets') x where x ->> 'url' = ?)",
              url
            ))
          .CountAsync()
        |> Handler.returnTask
    }

module private HashStore =
  let private getHashAddressedDirectory webRootPath =
    System.IO.Path.Join(webRootPath, "assets", "hash")
    |> fun dir ->
      Directory.CreateDirectory dir |> ignore
      dir

  let rawWriteHashAddressedAsset
    (fileName: string)
    (stream: Stream)
    (logger: ILogger<_>)
    webRootPath
    =
    task {
      logger.LogInformation "Checking hash"
      stream.Seek(0L, SeekOrigin.Begin) |> ignore

      let streamHash =
        System.Security.Cryptography.SHA256.HashData stream

      let extension = Path.GetExtension fileName

      let hashAddressedDirectory =
        getHashAddressedDirectory webRootPath

      let address =
        Path.Join(
          hashAddressedDirectory,
          System.Convert.ToHexStringLower streamHash
          + extension
        )

      logger.LogInformation "Write if needed"
      stream.Seek(0L, SeekOrigin.Begin) |> ignore

      try
        logger.LogInformation(
          "Creating file for {hash}",
          streamHash
        )

        use fileStream =
          new FileStream(address, FileMode.CreateNew)

        do! stream.CopyToAsync fileStream
      with :? IOException as e ->
        // This exception is thrown in with `FileMode.CreateNew`
        // if the file already exists.
        logger.LogWarning(
          "Uploaded file didn't result in a new hash.\n{e}",
          e
        )

      return address
    }

  let writeHashAddressedAsset
    (fileName: string)
    (stream: Stream)
    =
    handler {
      let! logger =
        Handler.plug<ILogger<AssetMetadata>, _> ()

      let! webRootPath =
        Handler.plug<IWebHostEnvironment, _> ()
        |> Handler.map (fun host -> host.WebRootPath)

      let! address =
        rawWriteHashAddressedAsset
          fileName
          stream
          logger
          webRootPath
        |> Handler.returnTask

      return! Handler.getHrefForFilePath address
    }

  let garbageCollectHashStore url =
    handler {
      let! refCount = Queries.getRefCount url

      let! webRootPath =
        Handler.plug<IWebHostEnvironment, _> ()
        |> Handler.map (fun host -> host.WebRootPath)

      if refCount = 0 then
        let filename = Path.GetFileName url

        let directory =
          getHashAddressedDirectory webRootPath

        File.Delete(Path.Join(directory, filename))
      else
        ()
    }

let private insertNewMetadata
  (file: Microsoft.AspNetCore.Http.IFormFile)
  category
  assetName
  =
  handler {
    let! url =
      HashStore.writeHashAddressedAsset
        file.FileName
        (file.OpenReadStream())

    use! session = DocStore.startSession ()

    session.Insert<AssetMetadata>
      { name = assetName
        owner = UserOwned
        subAssets = []
        category = category
        url = url }

    do! DocStore.saveChanges session
  }

let private upsertMetadata
  (file: Microsoft.AspNetCore.Http.IFormFile)
  category
  assetName
  subAssetName
  =
  handler {
    let! existing = Queries.assetByName assetName category

    match existing, subAssetName with
    | None, Some _ ->
      // Can't update the subassets of an asset that doesn't exist
      return!
        Handler.failure (
          Response.withStatusCode 400 >> Response.ofEmpty
        )
    | Some e, Some _ when e.owner = Shared ->
      // Can't update the subassets of an asset that doesn't belong to you
      return!
        Handler.failure (
          Response.withStatusCode 400 >> Response.ofEmpty
        )
    | Some e, Some sub ->
      let! url =
        HashStore.writeHashAddressedAsset
          file.FileName
          (file.OpenReadStream())

      let replaced, rest =
        e.subAssets
        |> List.partition (fun sa -> sa.name = sub)

      let updated =
        { e with
            subAssets = { name = sub; url = url } :: rest }

      use! session = DocStore.startSession ()
      session.Update<AssetMetadata> updated
      do! DocStore.saveChanges session

      match replaced with
      | [ r ] -> do! HashStore.garbageCollectHashStore r.url
      | _ -> ()
    | None, None ->
      do! insertNewMetadata file category assetName
    | Some e, None when e.owner = Shared ->
      do! insertNewMetadata file category assetName
    | Some e, None ->
      let! url =
        HashStore.writeHashAddressedAsset
          file.FileName
          (file.OpenReadStream())

      let updated = { e with url = url }
      use! session = DocStore.startSession ()
      session.Update<AssetMetadata> updated
      do! DocStore.saveChanges session
      do! HashStore.garbageCollectHashStore e.url
  }

let private deleteMetadata category =
  handler {
    let! hasValidToken =
      Handler.fromCtxTask Xsrf.validateToken

    do!
      if not hasValidToken then
        Handler.failure (
          Response.withStatusCode 400 >> Response.ofEmpty
        )
      else
        Handler.return' ()

    let! path = Handler.fromCtx Request.getRoute

    let! existing =
      Queries.assetByName (path.GetString "name") category
      |> Handler.map (
        Option.bind (fun s ->
          match s.owner with
          | UserOwned -> Some s
          | Shared -> None)
      )
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    use! session = DocStore.startSession ()

    session.Delete<AssetMetadata> existing.name

    do! DocStore.saveChanges session

    for url in
      existing.url
      :: (existing.subAssets |> List.map (fun sa -> sa.url)) do
      do! HashStore.garbageCollectHashStore url
  }

let private deleteSubAsset subAssetName category =
  handler {
    let! hasValidToken =
      Handler.fromCtxTask Xsrf.validateToken

    do!
      if not hasValidToken then
        Handler.failure (
          Response.withStatusCode 400 >> Response.ofEmpty
        )
      else
        Handler.return' ()

    let! path = Handler.fromCtx Request.getRoute

    let! existing =
      Queries.assetByName (path.GetString "name") category
      |> Handler.map (
        Option.bind (fun s ->
          match s.owner with
          | UserOwned -> Some s
          | Shared -> None)
      )
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let deleted, rest =
      existing.subAssets
      |> List.partition (fun sa -> sa.name = subAssetName)

    let updated = { existing with subAssets = rest }
    use! session = DocStore.startSession ()
    session.Update<AssetMetadata> updated
    do! DocStore.saveChanges session

    match deleted with
    | [ d ] -> do! HashStore.garbageCollectHashStore d.url
    | _ -> ()
  }

type PreseedAssets
  (
    hostEnvironment: IWebHostEnvironment,
    docStore: Marten.IDocumentStore,
    logger: ILogger<PreseedAssets>
  ) =
  interface IHostedService with
    member _.StopAsync _ = task { return () }

    member _.StartAsync _ =
      task {
        let getAssestsDirectory () =
          let contentRootPath = hostEnvironment.WebRootPath

          Path.Join(contentRootPath, "assets")

        let directoryToCategory (directory: string) =
          match Path.GetFileName directory with
          | "speakers" -> Some(SpeakerAsset, directory)
          | "scenes" -> Some(SceneAsset, directory)
          | "music" -> Some(MusicAsset, directory)
          | _ -> None

        let userDirectory dir =
          Directory.EnumerateDirectories dir
          |> Seq.choose directoryToCategory
          |> Seq.collect (fun (category, dir) ->
            Directory.EnumerateFiles dir
            |> Seq.filter (fun f ->
              Path.GetExtension f <> ""
              && not ((Path.GetFileName f).StartsWith "."))
            |> Seq.map (fun f -> category, f))


        let getHrefForFilePath filePath =
          "/"
          + Path.GetRelativePath(
            hostEnvironment.WebRootPath,
            filePath
          )

        let assetsDirectory = getAssestsDirectory ()
        use session = docStore.LightweightSession()

        for directory in
          Directory.EnumerateDirectories assetsDirectory do

          let owner =
            if Path.GetFileName directory = "shared" then
              Shared
            else
              UserOwned

          let tenantId =
            match owner with
            | Shared ->
              JasperFx.MultiTenancy.TenantId.DefaultTenantId
            | UserOwned -> Path.GetFileName directory

          let tenantSession = session.ForTenant tenantId

          for category, file in userDirectory directory do
            let name = Path.GetFileNameWithoutExtension file

            let existing =
              tenantSession
                .Query<AssetMetadata>()
                .Where(fun w -> w.name = name)
                .FirstOrDefault()
              |> Option.ofObj

            match existing with
            | Some _ -> ()
            | None ->
              use stream = File.OpenRead file

              let address =
                HashStore.rawWriteHashAddressedAsset
                  file
                  stream
                  logger
                  hostEnvironment.WebRootPath
                |> Async.AwaitTask
                |> Async.RunSynchronously
                |> getHrefForFilePath

              let subAssetsDir =
                Path.Join(Path.GetDirectoryName file, name)

              let subAssets =
                if Directory.Exists subAssetsDir then
                  logger.LogInformation(
                    "Sub asset directory {dir} found",
                    subAssetsDir
                  )

                  Directory.EnumerateFiles subAssetsDir
                  |> Seq.filter (fun f ->
                    Path.GetExtension f <> ""
                    && not (
                      (Path.GetFileName f).StartsWith "."
                    ))
                  |> Seq.map (fun f ->
                    use steam = File.OpenRead f

                    let address =
                      HashStore.rawWriteHashAddressedAsset
                        f
                        steam
                        logger
                        hostEnvironment.WebRootPath
                      |> Async.AwaitTask
                      |> Async.RunSynchronously
                      |> getHrefForFilePath

                    { name =
                        Path.GetFileNameWithoutExtension f
                      url = address })
                  |> Seq.toList
                else
                  logger.LogInformation(
                    "No sub asset directory {dir} found",
                    subAssetsDir
                  )

                  []

              tenantSession.Insert<AssetMetadata>
                { name =
                    Path.GetFileNameWithoutExtension file
                  owner = owner
                  subAssets = subAssets
                  category = category
                  url = address }

        return! session.SaveChangesAsync()
      }

type SpeakerEmoteImage = { emote: string; url: string }

type SpeakerImages =
  { name: string
    url: string
    emotes: SpeakerEmoteImage list
    assetOwner: AssetOwner }

let findSpeakers () =
  handler {
    let! speakers =
      Queries.assetsByCategory SpeakerAsset
      |> Handler.map (
        List.map (fun asset ->
          { name = asset.name
            url = asset.url
            emotes =
              asset.subAssets
              |> List.map (fun sa ->
                { emote = sa.name; url = sa.url })
            assetOwner = asset.owner })
      )

    return
      speakers
      |> List.sortByDescending (fun s -> s.assetOwner)
      |> List.sortBy (fun s -> s.name)
      |> List.distinctBy (fun s -> s.name)
  }

let private assetToSpeaker (asset: AssetMetadata) =
  { name = asset.name
    url = asset.url
    emotes =
      asset.subAssets
      |> List.map (fun sa ->
        { emote = sa.name; url = sa.url })
    assetOwner = asset.owner }

let findSpeaker name =
  Queries.assetByName name SpeakerAsset
  |> Handler.map (Option.map assetToSpeaker)

let findSpeakerAs guid name =
  Queries.assetByNameAs guid name SpeakerAsset
  |> Handler.map (Option.map assetToSpeaker)

let private listSpeakersView viewContext =
  handler {
    let! speakers = findSpeakers ()
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken ()

    let page cells =
      [ B.block
          []
          [ B.title [] "Speaker images"
            B.form
              token
              []
              [ B.fieldHorizontal
                  []
                  { label = Some "Upload speaker"
                    body =
                      [ B.field (fun info ->
                          { info with
                              input =
                                [ B.input
                                    [ _type_ "text"
                                      _name_ "name"
                                      _placeholder_ "Name" ] ] })
                        B.field (fun info ->
                          { info with
                              field = [ _hidden_ ]
                              input =
                                [ B.input
                                    [ _hidden_
                                      _type_ "text"
                                      _name_ "emote"
                                      _value_ "" ] ] })
                        _div
                          [ _class_ "file field" ]
                          [ _p
                              [ _class_
                                  "control is-flex-grow-1" ]
                              [ _label
                                  [ _class_ "file-label" ]
                                  [ _input
                                      [ _class_ "file-input"
                                        _onchange_
                                          "document.getElementById('file-name').innerHTML = this.files[0].name"
                                        _type_ "file"
                                        _accept_ "image/*"
                                        _name_ "image" ]
                                    _span
                                      [ _class_
                                          "file-cta is-flex-grow-1" ]
                                      [ _span
                                          [ _class_
                                              "file-label" ]
                                          [ _text
                                              "Choose a file" ] ]
                                    _span
                                      [ _class_ "file-name"
                                        _id_ "file-name" ]
                                      [ _text
                                          "My image file" ] ] ] ]
                        B.field (fun info ->
                          { info with
                              input =
                                [ B.button
                                    [ _type_ "submit"
                                      _class_
                                        $"{B.Mods.isPrimary} {B.Mods.isFullwidth}"
                                      Hx.targetCss "#body"
                                      Hx.select "#body"
                                      Hx.encodingMultipart
                                      HxMorph.morphOuterHtml
                                      Hx.post
                                        "/assets/speaker" ]
                                    [ _text
                                        "Create or Update" ] ] }) ] } ] ]
        assetGrid cells ]

    return
      speakers
      |> List.map (fun s ->
        B.card
          [ _style_ "height: 100%;" ]
          { image =
              [ B.image
                  [ _class_ "is-3by4" ]
                  [ _src_ s.url
                    _style_
                      "object-fit: cover; object-position: top;" ]
                match s.assetOwner with
                | Shared -> ()
                | UserOwned ->
                  yield!
                    [ B.delete
                        [ _class_ $"{B.Mods.isSmall}"
                          Hx.targetCss "#body"
                          Hx.select "#body"
                          HxMorph.morphOuterHtml
                          Hx.delete
                            $"/assets/speaker/{s.name}"
                          Hx.headers
                            [ token.HeaderName,
                              token.RequestToken ]
                          _style_
                            "position: absolute; right: 5px;top: 5px;" ] ] ]
            content = [ B.content [] [ _text s.name ] ] }
        |> B.encloseAttr
          _a
          [ _href_
              $"/assets/speaker/{System.Uri.EscapeDataString s.name}" ]
        |> B.enclose B.cell)
      |> page
      |> template "Speakers"
      |> Response.ofHtml
  }

let getSpeakerView name viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken ()

    let! speaker =
      findSpeaker name
      |> Handler.ofOption viewContext.notFound

    let updateForm =
      match speaker.assetOwner with
      | Shared ->
        [ B.subtitle [] "Shared speaker (can't be changed)" ]
      | UserOwned ->
        B.form
          token
          []
          [ B.fieldHorizontal
              []
              { label = Some "Upload emote"
                body =
                  [ B.field (fun info ->
                      { info with
                          field = [ _hidden_ ]
                          input =
                            [ B.input
                                [ _type_ "text"
                                  _hidden_
                                  _value_ speaker.name
                                  _name_ "name" ] ] })
                    B.field (fun info ->
                      { info with
                          input =
                            [ B.input
                                [ _type_ "text"
                                  _name_ "emote"
                                  _placeholder_ "emote" ] ] })
                    _div
                      [ _class_ "file field" ]
                      [ _p
                          [ _class_ "control is-flex-grow-1" ]
                          [ _label
                              [ _class_ "file-label" ]
                              [ _input
                                  [ _class_ "file-input"
                                    _onchange_
                                      "document.getElementById('file-name').innerHTML = this.files[0].name"
                                    _type_ "file"
                                    _accept_ "image/*"
                                    _name_ "image" ]
                                _span
                                  [ _class_
                                      "file-cta is-flex-grow-1" ]
                                  [ _span
                                      [ _class_ "file-label" ]
                                      [ _text
                                          "Choose a file" ] ]
                                _span
                                  [ _class_ "file-name"
                                    _id_ "file-name" ]
                                  [ _text "My image file" ] ] ] ]
                    B.field (fun info ->
                      { info with
                          input =
                            [ B.button
                                [ _type_ "submit"
                                  _class_ B.Mods.isPrimary
                                  Hx.targetCss "#body"
                                  Hx.select "#body"
                                  Hx.encodingMultipart
                                  HxMorph.morphOuterHtml
                                  Hx.post "/assets/speaker" ]
                                [ _text "Create or Update" ] ] })


                    ] } ]
        |> List.singleton

    let page cells =
      [ B.block
          []
          [ B.title [] $"{speaker.name}'s emotes"
            yield! updateForm ]
        assetGrid cells ]

    do!
      Handler.updateCtx (
        Response.withHxPushUrl
          $"/assets/speaker/{urlEncode name}"
      )

    return
      speaker.emotes
      |> List.map (fun s ->
        B.card
          [ _style_ "height: 100%;" ]
          { image =
              [ B.image
                  [ _class_ "is-3by4" ]
                  [ _src_ s.url
                    _style_
                      "object-fit: cover; object-position: top;" ]
                match speaker.assetOwner with
                | Shared -> ()
                | UserOwned ->
                  yield!
                    [ B.delete
                        [ _class_ $"{B.Mods.isSmall}"
                          Hx.targetCss "#body"
                          Hx.select "#body"
                          HxMorph.morphOuterHtml
                          Hx.delete
                            $"/assets/speaker/{urlEncode speaker.name}/{urlEncode s.emote}"
                          Hx.headers
                            [ token.HeaderName,
                              token.RequestToken ]
                          _style_
                            "position: absolute; right: 5px;top: 5px;" ] ]

                ]
            content = [ B.content [] [ _text s.emote ] ] }
        |> B.enclose B.cell)
      |> page
      |> template $"{speaker.name} emotes"
      |> Response.ofHtml
  }

let private postSpeaker =
  handler {
    let! formData =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let image = formData.TryGetFile "image"
          let name = formData.TryGetString "name"
          let emote = formData.TryGetStringNonEmpty "emote"

          Option.map2
            (fun image name ->
              {| image = image
                 name = name
                 emote = emote |})
            image
            name)

    do!
      upsertMetadata
        formData.image
        SpeakerAsset
        formData.name
        formData.emote

    let redirect =
      match formData.emote with
      | Some _ ->
        $"/assets/speaker/{System.Uri.EscapeDataString formData.name}"
      | None -> "/assets/speaker"

    return
      Response.withHxRedirect redirect >> Response.ofEmpty
  }
  |> Handler.flatten
  |> post "/assets/speaker"

let private deleteSpeaker =
  handler {
    do! deleteMetadata SpeakerAsset

    return
      Response.withHxRedirect "/assets/speaker"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/speaker/{name}"

let private deleteSpeakerEmote =
  handler {
    let! path = Handler.fromCtx Request.getRoute
    let name = path.GetString "name"
    do! deleteSubAsset (path.GetString "emote") SpeakerAsset

    return
      Response.withHxRedirect
        $"/assets/speaker/{System.Uri.EscapeDataString name}"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/speaker/{name}/{emote}"

let private listSpeakers viewContext =
  listSpeakersView viewContext
  |> Handler.flatten
  |> get "/assets/speaker"

let getSpeaker viewContext =
  handler {
    let! route = Handler.fromCtx Request.getRoute
    let name = route.GetString "name"
    return! getSpeakerView name viewContext
  }
  |> Handler.flatten
  |> get "/assets/speaker/{name}"

type SceneTagImage = { tag: string; url: string }

type SceneImages =
  { name: string
    url: string
    tags: SceneTagImage list
    assetOwner: AssetOwner }

  static member FromAssetMetadata x =
    { tags =
        x.subAssets
        |> List.map (fun a -> { tag = a.name; url = a.url })
      name = x.name
      assetOwner = x.owner
      url = x.url }

let findScenes () =
  handler {
    let! scenes = Queries.assetsByCategory SceneAsset

    return
      scenes
      |> List.map SceneImages.FromAssetMetadata
      |> List.sortByDescending (fun s -> s.assetOwner)
      |> List.sortBy (fun s -> s.name)
      |> List.distinctBy (fun s -> s.name)
  }

let findScene name =
  Queries.assetByName name SceneAsset
  |> Handler.map (Option.map SceneImages.FromAssetMetadata)

let findSceneAs guid name =
  Queries.assetByNameAs guid name SceneAsset
  |> Handler.map (Option.map SceneImages.FromAssetMetadata)

let private postScene =
  handler {
    let! formData =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let image = formData.TryGetFile "image"
          let name = formData.TryGetString "name"
          let tag = formData.TryGetStringNonEmpty "tag"

          Option.map2
            (fun image name ->
              {| image = image
                 name = name
                 tag = tag |})
            image
            name)

    do!
      upsertMetadata
        formData.image
        SceneAsset
        formData.name
        formData.tag

    let redirect =
      match formData.tag with
      | Some _ ->
        $"/assets/scene/{System.Uri.EscapeDataString formData.name}"
      | None -> "/assets/scene"

    return
      Response.withHxRedirect redirect >> Response.ofEmpty
  }
  |> Handler.flatten
  |> post "/assets/scene"

let private deleteScene =
  handler {
    do! deleteMetadata SceneAsset

    return
      Response.withHxRedirect "/assets/scene"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/scene/{name}"

let private deleteSceneTag =
  handler {
    let! path = Handler.fromCtx Request.getRoute
    let name = path.GetString "name"
    do! deleteSubAsset (path.GetString "tag") SceneAsset

    return
      Response.withHxRedirect
        $"/assets/scene/{System.Uri.EscapeDataString name}"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/scene/{name}/{tag}"

let private listScenes viewContext =
  handler {
    let! scenes = findScenes ()
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken ()

    let page cells =
      [ B.block
          []
          [ B.title [] "Scenes"
            B.form
              token
              []
              [ B.fieldHorizontal
                  []
                  { label = Some "Upload scene"
                    body =
                      [ B.field (fun info ->
                          { info with
                              input =
                                [ B.input
                                    [ _type_ "text"
                                      _name_ "name"
                                      _placeholder_ "Name" ] ] })
                        B.field (fun info ->
                          { info with
                              field = [ _hidden_ ]
                              input =
                                [ B.input
                                    [ _hidden_
                                      _type_ "text"
                                      _name_ "tag"
                                      _value_ "" ] ] })
                        _div
                          [ _class_ "file field" ]
                          [ _p
                              [ _class_
                                  "control is-flex-grow-1" ]
                              [ _label
                                  [ _class_ "file-label" ]
                                  [ _input
                                      [ _class_ "file-input"
                                        _onchange_
                                          "document.getElementById('file-name').innerHTML = this.files[0].name"
                                        _type_ "file"
                                        _accept_ "image/*"
                                        _name_ "image" ]
                                    _span
                                      [ _class_
                                          "file-cta is-flex-grow-1" ]
                                      [ _span
                                          [ _class_
                                              "file-label" ]
                                          [ _text
                                              "Choose a file" ] ]
                                    _span
                                      [ _class_ "file-name"
                                        _id_ "file-name" ]
                                      [ _text
                                          "My image file" ] ] ] ]
                        B.field (fun info ->
                          { info with
                              input =
                                [ B.button
                                    [ _type_ "submit"
                                      _class_
                                        $"{B.Mods.isPrimary} {B.Mods.isFullwidth}"
                                      Hx.targetCss "#body"
                                      Hx.select "#body"
                                      Hx.encodingMultipart
                                      HxMorph.morphOuterHtml
                                      Hx.post
                                        "/assets/scene" ]
                                    [ _text
                                        "Create or Update" ] ] }) ] } ] ]
        assetGrid cells ]

    return
      scenes
      |> List.map (fun s ->
        B.card
          [ _style_ "height: 100%;" ]
          { image =
              [ B.image
                  [ _class_ "is-3by4" ]
                  [ _src_ s.url
                    _style_
                      "object-fit: cover; object-position: top;" ]
                match s.assetOwner with
                | Shared -> ()
                | UserOwned ->
                  yield!
                    [ B.delete
                        [ _class_ $"{B.Mods.isSmall}"
                          Hx.targetCss "#body"
                          Hx.select "#body"
                          HxMorph.morphOuterHtml
                          Hx.delete
                            $"/assets/scene/{s.name}"
                          Hx.headers
                            [ token.HeaderName,
                              token.RequestToken ]
                          _style_
                            "position: absolute; right: 5px;top: 5px;" ] ] ]
            content = [ B.content [] [ _text s.name ] ] }
        |> B.encloseAttr
          _a
          [ _href_ $"/assets/scene/{s.name}" ]
        |> B.enclose B.cell)
      |> page
      |> template "Scenes"
      |> Response.ofHtml
  }
  |> Handler.flatten
  |> get "/assets/scene"

let getScene viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! route = Handler.fromCtx Request.getRoute
    let name = route.GetString "name"
    let! token = Handler.getCsrfToken ()

    let! scene =
      findScene name
      |> Handler.ofOption viewContext.notFound

    let updateForm =
      match scene.assetOwner with
      | Shared ->
        [ B.subtitle [] "Shared scene (can't be changed)" ]
      | UserOwned ->
        B.form
          token
          []
          [ B.fieldHorizontal
              []
              { label = Some "Upload tag"
                body =
                  [ B.field (fun info ->
                      { info with
                          field = [ _hidden_ ]
                          input =
                            [ B.input
                                [ _type_ "text"
                                  _hidden_
                                  _value_ scene.name
                                  _name_ "name" ] ] })
                    B.field (fun info ->
                      { info with
                          input =
                            [ B.input
                                [ _type_ "text"
                                  _name_ "tag"
                                  _placeholder_ "emote" ] ] })
                    _div
                      [ _class_ "file field" ]
                      [ _p
                          [ _class_ "control is-flex-grow-1" ]
                          [ _label
                              [ _class_ "file-label" ]
                              [ _input
                                  [ _class_ "file-input"
                                    _onchange_
                                      "document.getElementById('file-name').innerHTML = this.files[0].name"
                                    _type_ "file"
                                    _accept_ "image/*"
                                    _name_ "image" ]
                                _span
                                  [ _class_
                                      "file-cta is-flex-grow-1" ]
                                  [ _span
                                      [ _class_ "file-label" ]
                                      [ _text
                                          "Choose a file" ] ]
                                _span
                                  [ _class_ "file-name"
                                    _id_ "file-name" ]
                                  [ _text "My image file" ] ] ] ]
                    B.field (fun info ->
                      { info with
                          input =
                            [ B.button
                                [ _type_ "submit"
                                  _class_ B.Mods.isPrimary
                                  Hx.targetCss "#body"
                                  Hx.select "#body"
                                  Hx.encodingMultipart
                                  HxMorph.morphOuterHtml
                                  Hx.post "/assets/scene" ]
                                [ _text "Create or Update" ] ] })


                    ] } ]
        |> List.singleton

    let page cells =
      [ B.block
          []
          [ B.title [] $"{scene.name}'s tags"
            yield! updateForm ]
        assetGrid cells ]

    return
      scene.tags
      |> List.map (fun t ->
        B.card
          [ _style_ "height: 100%;" ]
          { image =
              [ B.image
                  [ _class_ "is-3by4" ]
                  [ _src_ t.url
                    _style_
                      "object-fit: cover; object-position: top;" ]
                match scene.assetOwner with
                | Shared -> ()
                | UserOwned ->
                  yield!
                    [ B.delete
                        [ _class_ $"{B.Mods.isSmall}"
                          Hx.targetCss "#body"
                          Hx.select "#body"
                          HxMorph.morphOuterHtml
                          Hx.delete
                            $"/assets/scene/{scene.name}/{t.tag}"
                          Hx.headers
                            [ token.HeaderName,
                              token.RequestToken ]
                          _style_
                            "position: absolute; right: 5px;top: 5px;" ] ]

                ]
            content = [ B.content [] [ _text t.tag ] ] }
        |> B.enclose B.cell)
      |> page
      |> template $"{scene.name} emotes"
      |> Response.ofHtml
  }
  |> Handler.flatten
  |> get "/assets/scene/{name}"

type Music =
  { name: string
    url: string
    assetOwner: AssetOwner }

  static member ToAssetMetadata x =
    { name = x.name
      url = x.url
      subAssets = []
      category = MusicAsset
      owner = x.assetOwner }

  static member FromAssetMetadata x =
    { assetOwner = x.owner
      name = x.name
      url = x.url }

let findAllMusic () =
  handler {
    let! music = Queries.assetsByCategory MusicAsset

    return
      music
      |> List.map Music.FromAssetMetadata
      |> List.sortByDescending (fun s -> s.assetOwner)
      |> List.sortBy (fun s -> s.name)
      |> List.distinctBy (fun s -> s.name)
  }

let findMusic name =
  Queries.assetByName name MusicAsset
  |> Handler.map (Option.map Music.FromAssetMetadata)

let findMusicAs guid name =
  Queries.assetByNameAs guid name MusicAsset
  |> Handler.map (Option.map Music.FromAssetMetadata)

let private postMusic =
  handler {
    let! formData =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let music = formData.TryGetFile "audio"
          let name = formData.TryGetString "name"

          Option.map2
            (fun music name ->
              {| music = music; name = name |})
            music
            name)

    do!
      upsertMetadata
        formData.music
        MusicAsset
        formData.name
        None

    return
      Response.withHxRedirect "/assets/music"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> post "/assets/music"

let private deleteMusic =
  handler {
    do! deleteMetadata MusicAsset

    return
      Response.withHxRedirect "/assets/music"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/music/{name}"

let private listMusic viewContext =
  handler {
    let! music = findAllMusic ()
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken ()

    let page cells =
      [ B.block
          []
          [ B.title [] "Music"
            B.form
              token
              []
              [ B.fieldHorizontal
                  []
                  { label = Some "Upload music"
                    body =
                      [ B.field (fun info ->
                          { info with
                              input =
                                [ B.input
                                    [ _type_ "text"
                                      _name_ "name"
                                      _placeholder_ "Name" ] ] })
                        _div
                          [ _class_ "file field" ]
                          [ _p
                              [ _class_
                                  "control is-flex-grow-1" ]
                              [ _label
                                  [ _class_ "file-label" ]
                                  [ _input
                                      [ _class_ "file-input"
                                        _onchange_
                                          "document.getElementById('file-name').innerHTML = this.files[0].name"
                                        _type_ "file"
                                        _accept_ "audio/*"
                                        _name_ "audio" ]
                                    _span
                                      [ _class_
                                          "file-cta is-flex-grow-1" ]
                                      [ _span
                                          [ _class_
                                              "file-label" ]
                                          [ _text
                                              "Choose a file" ] ]
                                    _span
                                      [ _class_ "file-name"
                                        _id_ "file-name" ]
                                      [ _text
                                          "My music file" ] ] ] ]
                        B.field (fun info ->
                          { info with
                              input =
                                [ B.button
                                    [ _type_ "submit"
                                      _class_
                                        $"{B.Mods.isPrimary} {B.Mods.isFullwidth}"
                                      Hx.targetCss "#body"
                                      Hx.select "#body"
                                      Hx.encodingMultipart
                                      HxMorph.morphOuterHtml
                                      Hx.post
                                        "/assets/music" ]
                                    [ _text
                                        "Create or Update" ] ] }) ] } ] ]
        assetGrid cells ]

    return
      music
      |> List.map (fun s ->
        B.card
          [ _style_ "height: 100%;" ]
          { image =
              [ B.image
                  [ _class_ "is-3by4" ]
                  [ _src_ "/flying_score.svg"
                    _style_
                      "object-fit: cover; object-position: left; background-color: white;" ]
                match s.assetOwner with
                | Shared -> ()
                | UserOwned ->
                  yield!
                    [ B.delete
                        [ _class_ $"{B.Mods.isSmall}"
                          Hx.targetCss "#body"
                          Hx.select "#body"
                          HxMorph.morphOuterHtml
                          Hx.delete
                            $"/assets/music/{s.name}"
                          Hx.headers
                            [ token.HeaderName,
                              token.RequestToken ]
                          _style_
                            "position: absolute; right: 5px;top: 5px;" ] ] ]
            content =
              [ _audio
                  [ _id_ $"music-{s.name}"; _src_ s.url ]
                  []
                B.content [] [ _text s.name ] ] }
        |> B.encloseAttr
          _a
          [ _onclick_
              $"[...document.querySelectorAll('audio')].map((a) => {{ if('music-{s.name}' === a.getAttribute('id')) {{ if(a.paused || a.currentTime === 0) {{ htmx.closest(a, '.card').classList.add('has-text-primary'); a.play() }} else {{ htmx.closest(a, '.card').classList.remove('has-text-primary'); a.pause() }} }} else {{ htmx.closest(a, '.card').classList.remove('has-text-primary'); a.pause() }} }})" ]
        |> B.enclose B.cell)
      |> page
      |> template "Music"
      |> Response.ofHtml
  }
  |> Handler.flatten
  |> get "/assets/music"


let speakerNav =
  B.navbarItemA
    [ _href_ "/assets/speaker" ]
    [ _text "Speakers" ]

let sceneNav =
  B.navbarItemA
    [ _href_ "/assets/scene" ]
    [ _text "Scenes" ]

let musicNav =
  B.navbarItemA [ _href_ "/assets/music" ] [ _text "Music" ]

module Service =
  open Marten
  open Microsoft.Extensions.DependencyInjection

  let endpoints viewContext =
    [ postSpeaker
      deleteSpeaker
      deleteSpeakerEmote
      listSpeakers viewContext
      getSpeaker viewContext
      postScene
      deleteScene
      deleteSceneTag
      listScenes viewContext
      getScene viewContext
      deleteMusic
      postMusic
      listMusic viewContext ]

  let addService: AddService =
    fun _ sc ->
      sc
        .AddSingleton<PreseedAssets>()
        .AddHostedService(fun x ->
          x.GetRequiredService<PreseedAssets>())
      |> ignore

      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema
          .For<AssetMetadata>()
          .MultiTenanted()
        |> ignore)
