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

module B = Bulma

let private assetGrid cells =
  B.block
    []
    [ B.fixedGrid
        [ _class_
            "has-6-cols-widescreen has-4-cols-tablet has-1-cols-mobile" ]
        cells ]

let private getAssestsDirectory =
  handler {
    let! user = User.ensureSessionUser

    let! hostEnvironment =
      Handler.plug<IWebHostEnvironment> ()

    let contentRootPath = hostEnvironment.WebRootPath

    return
      System.IO.Path.Join(
        contentRootPath,
        "assets",
        user.id.ToString()
      )
  }
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

let private getSharedAssestsDirectory =
  handler {
    let! hostEnvironment =
      Handler.plug<IWebHostEnvironment> ()

    let contentRootPath = hostEnvironment.WebRootPath

    return
      System.IO.Path.Join(
        contentRootPath,
        "assets",
        "shared"
      )
  }
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

type AssetOwner =
  | Shared
  | UserOwned

type SpeakerEmoteImage = { emote: string; url: string }

type SpeakerImages =
  { name: string
    url: string
    emotes: SpeakerEmoteImage list
    assetOwner: AssetOwner }

let private fileFilter (file: string) =
  Path.GetFileNameWithoutExtension file <> ""
  && not (file.StartsWith ".")

let private meaningfulFileFilter map files =
  files |> Seq.filter (fun f -> fileFilter (map f))

let private getSpeakersDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "speakers"))
    getAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

let private getSharedSpeakersDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "speakers"))
    getSharedAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

let findSpeakers =
  handler {
    let! speakersDirectory = getSpeakersDirectory

    let! sharedSpeakersDirectory =
      getSharedSpeakersDirectory

    let baseImages =
      [ Directory.EnumerateFiles speakersDirectory
        |> Seq.map (fun s -> s, UserOwned)
        Directory.EnumerateFiles sharedSpeakersDirectory
        |> Seq.map (fun s -> s, Shared) ]
      |> Seq.concat
      |> meaningfulFileFilter fst

    return!
      baseImages
      |> Handler.collect (fun (baseImage, owner) ->
        handler {
          let name =
            Path.GetFileNameWithoutExtension baseImage

          let baseImagePath = baseImage

          let emoteDir =
            Path.Join(
              Path.GetDirectoryName baseImagePath,
              Path.GetFileNameWithoutExtension
                baseImagePath
            )

          let! url =
            Handler.getHrefForFilePath baseImagePath

          let! emotes =
            if Directory.Exists emoteDir then
              emoteDir
              |> Directory.EnumerateFiles
              |> meaningfulFileFilter id
              |> Handler.collect (fun f ->
                handler {
                  let! emoteUrl =
                    Handler.getHrefForFilePath f

                  return
                    { emote =
                        Path.GetFileNameWithoutExtension f
                      url = emoteUrl }
                })
            else
              Handler.return' []

          return
            { name = name
              url = url
              emotes = emotes
              assetOwner = owner }
        })
      |> Handler.map (
        List.sortByDescending (fun s -> s.assetOwner)
        >> List.sortBy (fun s -> s.name)
        >> List.distinctBy (fun s -> s.name)
      )
  }

let findSpeaker name =
  findSpeakers
  |> Handler.map (List.filter (fun s -> s.name = name))
  |> Handler.map
    // Prefer the user's own image if there's a name
    // clash with the public images to allow for user
    // customization
    (fun matching ->
      match matching with
      | onlyOne :: [] -> Some onlyOne
      | [] -> None
      | userOwned :: _
      | _ :: userOwned :: _ when
        userOwned.assetOwner = UserOwned
        ->
        Some userOwned
      | shared :: _ -> Some shared)

let private postSpeaker =
  handler {
    let! speakersDirectory = getSpeakersDirectory

    return!
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let image = formData.TryGetFile "image"
          let name = formData.TryGetString "name"
          let emote = formData.TryGetStringNonEmpty "emote"

          match name, emote, image with
          | Some name, Some e, Some image ->
            let speakerDir =
              Path.Join(speakersDirectory, name)

            Directory.CreateDirectory speakerDir |> ignore

            let emoteFilename =
              e + Path.GetExtension image.FileName

            let fileName =
              Path.Join(speakerDir, emoteFilename)

            use fileStream =
              new FileStream(fileName, FileMode.Create)

            image.CopyTo fileStream

            Response.withHxRedirect
              $"/assets/speaker/{name}"
            >> Response.ofEmpty
            |> Some
          | Some name, None, Some image ->

            let fileName =
              System.IO.Path.Join(
                speakersDirectory,
                name
                + System.IO.Path.GetExtension
                    image.FileName
              )

            use fileStream =
              new FileStream(fileName, FileMode.Create)

            image.CopyTo fileStream

            Response.withHxRedirect "/assets/speaker"
            >> Response.ofEmpty
            |> Some
          | _ -> None)
  }
  |> Handler.flatten
  |> post "/assets/speaker"

let private deleteSpeaker =
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

    let! speaker =
      findSpeaker (path.GetString "name")
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let! speakersDirectory = getSpeakersDirectory

    // We're deleting the whole speaker
    let speakerDir =
      Path.Join(speakersDirectory, speaker.name)

    let fileName =
      System.IO.Path.Join(
        speakersDirectory,
        speaker.name + Path.GetExtension speaker.url
      )

    File.Delete fileName

    do
      if Directory.Exists speakerDir then
        Directory.Delete(speakerDir, true)

    return
      Response.withHxRedirect "/assets/speaker"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/speaker/{name}"

let private deleteSpeakerEmote =
  handler {
    let! hasValidToken =
      Handler.fromCtxTask Xsrf.validateToken

    do!
      if not hasValidToken then
        Handler.failure (
          Response.withStatusCode 400
          >> Response.ofHtmlString "Invalid token"

        )
      else
        Handler.return' ()

    let! path = Handler.fromCtx Request.getRoute
    let name = path.GetString "name"
    let emote = path.GetString "emote"

    let! speaker =
      findSpeaker name
      |> Handler.ofOption (
        Response.withStatusCode 400
        >> Response.ofHtmlString
          $"Didn't find speaker {name}"
      )

    let! speakersDirectory = getSpeakersDirectory
    // We're only deleting one emote
    let speakerDir =
      Path.Join(speakersDirectory, speaker.name)

    let emoteFilename =
      emote + Path.GetExtension speaker.url

    let fileName = Path.Join(speakerDir, emoteFilename)
    File.Delete fileName

    return
      Response.withHxRedirect
        $"/assets/speaker/{speaker.name}"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/speaker/{name}/{emote}"

let private listSpeakers viewContext =
  handler {
    let! speakers = findSpeakers
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken

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
          [ _href_ $"/assets/speaker/{s.name}" ]
        |> B.enclose B.cell)
      |> page
      |> template "Speakers"
      |> Response.ofHtml
  }
  |> Handler.flatten
  |> get "/assets/speaker"

let getSpeaker viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! route = Handler.fromCtx Request.getRoute
    let name = route.GetString "name"
    let! token = Handler.getCsrfToken

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
                            $"/assets/speaker/{speaker.name}/{s.emote}"
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
  |> Handler.flatten
  |> get "/assets/speaker/{name}"

let private getScenesDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "scenes"))
    getAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

let private getSharedScenesDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "scenes"))
    getSharedAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

type SceneTagImage = { tag: string; url: string }

type SceneImages =
  { name: string
    url: string
    tags: SceneTagImage list
    assetOwner: AssetOwner }

let private postScene =
  handler {
    let! scenesDirectory = getScenesDirectory

    return!
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let image = formData.TryGetFile "image"
          let name = formData.TryGetString "name"
          let tag = formData.TryGetStringNonEmpty "tag"

          match name, tag, image with
          | Some name, Some e, Some image ->
            let sceneDir = Path.Join(scenesDirectory, name)

            Directory.CreateDirectory sceneDir |> ignore

            let emoteFilename =
              e + Path.GetExtension image.FileName

            let fileName =
              Path.Join(sceneDir, emoteFilename)

            use fileStream =
              new FileStream(fileName, FileMode.Create)

            image.CopyTo fileStream

            Response.withHxRedirect $"/assets/scene/{name}"
            >> Response.ofEmpty
            |> Some
          | Some name, None, Some image ->

            let fileName =
              System.IO.Path.Join(
                scenesDirectory,
                name
                + System.IO.Path.GetExtension
                    image.FileName
              )

            use fileStream =
              new FileStream(fileName, FileMode.Create)

            image.CopyTo fileStream

            Response.withHxRedirect "/assets/scene"
            >> Response.ofEmpty
            |> Some
          | _ -> None)
  }
  |> Handler.flatten
  |> post "/assets/scene"

let findScenes =
  handler {
    let! scenesDirectory = getScenesDirectory

    let! sharedScenesDirectory = getSharedScenesDirectory

    let baseImages =
      [ Directory.EnumerateFiles scenesDirectory
        |> Seq.map (fun s -> s, UserOwned)
        Directory.EnumerateFiles sharedScenesDirectory
        |> Seq.map (fun s -> s, Shared) ]
      |> Seq.concat
      |> meaningfulFileFilter fst

    return!
      baseImages
      |> Handler.collect (fun (baseImage, owner) ->
        handler {
          let name =
            Path.GetFileNameWithoutExtension baseImage

          let baseImagePath = baseImage

          let tagDir =
            Path.Join(
              Path.GetDirectoryName baseImagePath,
              Path.GetFileNameWithoutExtension
                baseImagePath
            )

          let! url =
            Handler.getHrefForFilePath baseImagePath

          let! tags =
            if Directory.Exists tagDir then
              tagDir
              |> Directory.EnumerateFiles
              |> meaningfulFileFilter id
              |> Handler.collect (fun f ->
                handler {
                  let! tagUrl =
                    Handler.getHrefForFilePath f

                  return
                    { tag =
                        Path.GetFileNameWithoutExtension f
                      url = tagUrl }
                })
            else
              Handler.return' []

          return
            { name = name
              url = url
              tags = tags
              assetOwner = owner }
        })
      |> Handler.map (
        List.sortByDescending (fun s -> s.assetOwner)
        >> List.sortBy (fun s -> s.name)
        >> List.distinctBy (fun s -> s.name)
      )
  }

let findScene name =
  findScenes
  |> Handler.map (List.filter (fun s -> s.name = name))
  |> Handler.map
    // Prefer the user's own image if there's a name
    // clash with the public images to allow for user
    // customization
    (fun matching ->
      match matching with
      | onlyOne :: [] -> Some onlyOne
      | [] -> None
      | userOwned :: _
      | _ :: userOwned :: _ when
        userOwned.assetOwner = UserOwned
        ->
        Some userOwned
      | shared :: _ -> Some shared)

let private deleteScene =
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

    let! scene =
      findScene (path.GetString "name")
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let! scenesDirectory = getScenesDirectory

    // We're deleting the whole scene
    let sceneDir = Path.Join(scenesDirectory, scene.name)

    let fileName =
      System.IO.Path.Join(
        scenesDirectory,
        scene.name + Path.GetExtension scene.url
      )

    File.Delete fileName

    do
      if Directory.Exists sceneDir then
        Directory.Delete(sceneDir, true)

    return
      Response.withHxRedirect "/assets/scene"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/scene/{name}"

let private deleteSceneTag =
  handler {
    let! hasValidToken =
      Handler.fromCtxTask Xsrf.validateToken

    do!
      if not hasValidToken then
        Handler.failure (
          Response.withStatusCode 400
          >> Response.ofHtmlString "Invalid token"

        )
      else
        Handler.return' ()

    let! path = Handler.fromCtx Request.getRoute
    let name = path.GetString "name"
    let tag = path.GetString "tag"

    let! scene =
      findScene name
      |> Handler.ofOption (
        Response.withStatusCode 400
        >> Response.ofHtmlString $"Didn't find scene {name}"
      )

    let! scenesDirectory = getScenesDirectory
    // We're only deleting one tag
    let sceneDir = Path.Join(scenesDirectory, scene.name)

    let tagFilename = tag + Path.GetExtension scene.url

    let fileName = Path.Join(sceneDir, tagFilename)
    File.Delete fileName

    return
      Response.withHxRedirect $"/assets/scene/{scene.name}"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/scene/{name}/{tag}"

let private listScenes viewContext =
  handler {
    let! scenes = findScenes
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken

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
    let! token = Handler.getCsrfToken

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

let private getMusicDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "music"))
    getAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

let private getSharedMusicDirectory =
  Handler.map
    (fun assets -> Path.Join(assets, "music"))
    getSharedAssestsDirectory
  |> Handler.tap (
    Directory.CreateDirectory >> ignore >> Handler.return'
  )

type Music =
  { name: string
    url: string
    assetOwner: AssetOwner }

let private postMusic =
  handler {
    let! musicDirectory = getMusicDirectory

    return!
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun formData ->
          let music = formData.TryGetFile "audio"
          let name = formData.TryGetString "name"

          match name, music with
          | Some name, Some music ->

            let fileName =
              System.IO.Path.Join(
                musicDirectory,
                name
                + System.IO.Path.GetExtension
                    music.FileName
              )

            use fileStream =
              new FileStream(fileName, FileMode.Create)

            music.CopyTo fileStream

            Response.withHxRedirect "/assets/music"
            >> Response.ofEmpty
            |> Some
          | _ -> None)
  }
  |> Handler.flatten
  |> post "/assets/music"

let findAllMusic =
  handler {
    let! musicDirectory = getMusicDirectory

    let! sharedMusicDirectory = getSharedMusicDirectory

    let musicFiles =
      [ Directory.EnumerateFiles musicDirectory
        |> Seq.map (fun s -> s, UserOwned)
        Directory.EnumerateFiles sharedMusicDirectory
        |> Seq.map (fun s -> s, Shared) ]
      |> Seq.concat
      |> meaningfulFileFilter fst

    return!
      musicFiles
      |> Handler.collect (fun (musicFile, owner) ->
        handler {
          let name =
            Path.GetFileNameWithoutExtension musicFile

          let! url = Handler.getHrefForFilePath musicFile


          return
            { name = name
              url = url
              assetOwner = owner }
        })
      |> Handler.map (
        List.sortByDescending (fun s -> s.assetOwner)
        >> List.sortBy (fun s -> s.name)
        >> List.distinctBy (fun s -> s.name)
      )
  }

let findMusic name =
  findAllMusic
  |> Handler.map (List.filter (fun s -> s.name = name))
  |> Handler.map
    // Prefer the user's own image if there's a name
    // clash with the public images to allow for user
    // customization
    (fun matching ->
      match matching with
      | onlyOne :: [] -> Some onlyOne
      | [] -> None
      | userOwned :: _
      | _ :: userOwned :: _ when
        userOwned.assetOwner = UserOwned
        ->
        Some userOwned
      | shared :: _ -> Some shared)

let private deleteMusic =
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

    let! music =
      findMusic (path.GetString "name")
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let! musicDirectory = getMusicDirectory

    let fileName =
      System.IO.Path.Join(
        musicDirectory,
        music.name + Path.GetExtension music.url
      )

    File.Delete fileName

    return
      Response.withHxRedirect "/assets/music"
      >> Response.ofEmpty
  }
  |> Handler.flatten
  |> delete "/assets/music/{name}"

let private listMusic viewContext =
  handler {
    let! music = findAllMusic
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken

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

  let addService = fun _ sc -> sc
