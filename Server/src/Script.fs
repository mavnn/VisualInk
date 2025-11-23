module VisualInk.Server.Script

open Marten
open Falco
open Falco.Markup
open Falco.Htmx
open Prelude
open Falco.Routing
open View
open System.Text.RegularExpressions
open System.Text.Json.Serialization
open System.Linq
open Microsoft.AspNetCore.Hosting

module B = Bulma

[<JsonFSharpConverter>]
type ScriptStats = { words: int; choices: int }

[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type Script =
  { id: System.Guid
    ink: string
    inkJson: string
    stats: ScriptStats option
    title: string
    publishedUrl: string option
    [<JsonIgnore>]
    mutable writerId: string }

type CompilationResult =
  | CompiledStory of Ink.Runtime.Story * ScriptStats option
  | CompilationErrors of
    (string * Ink.ErrorType) seq *
    ScriptStats option

type ScriptFileHandler
  (docStore: IDocumentStore, userService: User.IUserService)
  =
  interface Ink.IFileHandler with
    member _.ResolveInkFilename includeName = includeName

    member _.LoadInkFileContents
      (fullFilename: string)
      : string =
      let userId = userService.GetUserId()

      match userId with
      | Some uid ->
        let session = docStore.QuerySession(uid.ToString())

        let script =
          session
            .Query<Script>()
            .Where(fun s -> s.title = fullFilename)
            .OrderBySql("mt_last_modified desc")
            .First()

        script.ink
      | None ->
        failwithf "Script '%s' not found" fullFilename


let private compile fileHandler title ink =
  let errors =
    new System.Collections.Generic.List<
      string * Ink.ErrorType
     >()

  let errorHandler: Ink.ErrorHandler =
    Ink.ErrorHandler(fun err t -> errors.Add(err, t))

  let compiler =
    new Ink.Compiler(
      ink,
      new Ink.Compiler.Options(
        sourceFilename = title,
        errorHandler = errorHandler,
        fileHandler = fileHandler
      )
    )

  let parsed = compiler.Parse()
  let story = compiler.Compile()

  let stats =
    Option.ofObj parsed
    |> Option.map Ink.Stats.Generate
    |> Option.map (fun s ->
      { words = s.words; choices = s.choices })

  if errors.Count > 0 then
    CompilationErrors(errors, stats)
  else
    CompiledStory(story, stats)

type ScriptTemplate = { title: string; ink: string }

let private getTemplateList () =
  handler {
    let! hostEnvironment =
      Handler.plug<IWebHostEnvironment, _> ()

    let contentRootPath = hostEnvironment.WebRootPath

    return
      System.IO.Path.Join(
        contentRootPath,
        "assets",
        "shared",
        "templates"
      )
      |> fun path ->
        System.IO.Directory.EnumerateFiles(path, "*.ink")
        |> Seq.map
          System.IO.Path.GetFileNameWithoutExtension
        |> Seq.toList
  }

let private getTemplate title =
  handler {
    let! hostEnvironment =
      Handler.plug<IWebHostEnvironment, _> ()

    let contentRootPath = hostEnvironment.WebRootPath

    let! content =
      Handler.returnTask (
        System.IO.File.ReadAllTextAsync(
          System.IO.Path.Join(
            contentRootPath,
            "assets",
            "shared",
            "templates",
            title + ".ink"
          )
        )
      )

    return { title = title; ink = content }
  }


let create (input: ScriptTemplate) =
  handler {
    use! session = DocStore.startSession ()
    let! user = User.ensureSessionUser ()

    let id = System.Guid.NewGuid()
    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()
    let story = compile fileHandler input.title input.ink

    match story with
    | CompiledStory(story, stats) ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = input.ink
          inkJson = inkJson
          stats = stats
          title = input.title
          publishedUrl = None
          writerId = user.id.ToString() }

      session.Insert<Script> script
      do! session.SaveChangesAsync() |> Handler.returnTask'
      return script
    // Save the actual script even if there are errors
    // that will prevent it running
    | CompilationErrors(_, stats) ->
      let script =
        { id = id
          ink = input.ink
          inkJson = "{}"
          stats = stats
          title = input.title
          publishedUrl = None
          writerId = user.id.ToString() }

      session.Insert<Script> script
      do! DocStore.saveChanges session
      return script
  }

let save (script: Script) =
  handler {
    use! session = DocStore.startSession ()

    session.Update<Script> script
    do! DocStore.saveChanges session
  }

let load (guid: System.Guid) =
  DocStore.load<Script, System.Guid, _> guid

type EditorInput =
  | DemoEditor of {| ink: string; title: string |}
  | UserEditor of Script


let editor input =
  handler {
    let! token = Handler.getCsrfToken ()

    let title =
      match input with
      | DemoEditor de -> de.title
      | UserEditor ue -> ue.title

    let ink =
      match input with
      | DemoEditor de -> de.ink
      | UserEditor ue -> ue.ink

    let controls =
      match input with
      | UserEditor existing ->
        B.buttons
          []
          [ [ _id_ "run-button"
              Hx.post "/playthrough/start"
              HxMorph.morphOuterHtml
              Hx.select "#page"
              Hx.targetCss "#page"
              _disabled_
              _name_ "script"
              _class_ B.Mods.isPrimary
              _value_ (existing.id.ToString()) ],
            [ _text "Run" ]
            match existing.publishedUrl with
            | None ->
              [ _id_ "publish"
                Hx.confirm
                  "Are you sure? This makes your game available publicly."
                Hx.select "#page"
                Hx.targetCss "#page"
                Hx.post "/script/publish"
                _name_ "publish"
                _class_ B.Mods.isDanger
                _value_ (existing.id.ToString()) ],
              [ _text "Publish" ]
            | Some _ ->
              [ _id_ "unpublish"
                Hx.confirm
                  "Are you sure? Your game will be unavailable until you publish it again."
                Hx.select "#page"
                Hx.targetCss "#page"
                Hx.post "/script/unpublish"
                _name_ "unpublish"
                _class_ B.Mods.isDanger
                _value_ (existing.id.ToString()) ],
              [ _text "Unpublish" ] ]
      | DemoEditor _ ->
        B.button
          [ _id_ "run-button"
            Attr.create
              "hx-on:htmx:config-request"
              "let ink = fe.getEditor().state.doc.toString(); event.detail.parameters.ink = ink; localStorage.setItem('ink', ink); localStorage.setItem('title', document.getElementById('story-title').value);"
            Hx.post "/playground/playthrough"
            Hx.select "#page"
            Hx.targetCss "#page"
            _disabled_
            _name_ "Script"
            _class_ B.Mods.isPrimary ]
          [ _text "Test your script" ]

    B.form
      token
      [ _id_ "editor-form"
        _class_
          "is-flex is-flex-direction-column is-items-align-stretch"
        _style_ "height: 100%;" ]
      [ B.field (fun info ->
          { info with
              label = Some "Title"
              field = [ _class_ "is-flex-grow-0" ]
              input =
                [ _input
                    [ _class_ "input"
                      _type_ "text"
                      _name_ "title"
                      _id_ "story-title"
                      _value_ title
                      _onkeyup_
                        "document.getElementById('run-button').setAttribute('disabled', true);"
                      _onblur_
                        "fe.callLinter(fe.getEditor());"
                      _placeholder_ "Your script title" ] ] })
        match input with
        | DemoEditor _ ->
          _p
            [ _class_ "has-text-danger mb-2" ]
            [ _text
                "To save changes and add images, you'll need to create an account." ]
        | UserEditor { publishedUrl = Some url } ->
          B.notification
            [ _class_ B.Mods.isPrimary ]
            [ _text "This script is published at "
              _a [ _href_ url ] [ _text url ]
              _text
                " and any changes you make will be immediately visible." ]
        | UserEditor _ -> _text ""
        _div [ _hidden_; _id_ "last-saved" ] [ _text ink ]
        _div
          [ _class_
              "field is-flex-grow-1 is-flex is-flex-direction-column is-items-align-stretch" ]
          [ _label
              [ _class_ "label" ]
              [ _text "The script" ]
            _div
              [ _id_ "codemirror"
                _class_ "control"
                _style_ "height: 100%;" ]
              [ _script [] [ _text "fe.addEditor()" ] ] ]
        _div
          [ _class_
              "field is-grouped is-grouped-centered is-flex-grow-0" ]
          [ _div
              [ _class_ "control" ]
              [ _div [ _class_ "control" ] [ controls ] ] ] ]

  }

let scriptManager (editorInput: EditorInput) =
  handler {
    let! form = editor editorInput

    let! speakers = StoryAssets.findSpeakers ()

    let encode v =
      v
      |> System.Text.Json.JsonSerializer.Serialize
      |> System.Net.WebUtility.HtmlEncode

    let makeMenu
      title
      subItemName
      (items:
        {| name: string
           subItems: string list |} list)
      =
      items
      |> List.map (fun item ->
        match item.subItems with
        | [] ->
          [ _a
              [ _onclick_ $"fe.addText({encode item.name})" ]
              [ _text item.name ] ]
        | subItems ->
          [ _a
              [ _onclick_ $"fe.addText({encode item.name})" ]
              [ _text item.name ]
            _details
              []
              [ _summary [] [ _text subItemName ]
                _ul
                  []
                  (subItems
                   |> List.map (fun i ->
                     _li
                       []
                       [ _a
                           [ _onclick_
                               $"fe.addText({encode i})" ]
                           [ _text i ] ])) ] ])
      |> fun list ->
          [ B.title [ _class_ "is-6" ] title
            B.content
              []
              (List.intersperse
                [ _hr [ _class_ "mt-1 mb-1" ] ]
                list
               |> List.concat) ]


    let speakerMenu =
      speakers
      |> List.map (fun speaker ->
        {| name = speaker.name
           subItems =
            speaker.emotes |> List.map (fun e -> e.emote) |})
      |> makeMenu "Speakers" "Emotes"

    let! scenes = StoryAssets.findScenes ()

    let sceneMenu =
      scenes
      |> List.map (fun scene -> {| name = scene.name; subItems = scene.tags |> List.map (fun s -> s.tag)|})
      |> makeMenu "Scenes" "Tags"

    let! music = StoryAssets.findAllMusic ()

    let musicMenu =
      music
      |> List.map (fun music -> {| name = music.name; subItems = []|})
      |> makeMenu "Music" ""

    let sideBar = _div [] (List.concat [speakerMenu; sceneMenu ; musicMenu ])

    return
      _div
        [ _id_ "editor" ]
        [ B.columns
            []
            [ B.encloseAttr
                B.column
                [ _class_ "is-three-quarters"
                  _style_ "height: 100vh;" ]
                form
              B.encloseAttr
                B.column
                [ _style_
                    "overflow-y: auto; max-height: 100vh;" ]
                sideBar ] ]
      |> List.singleton
  }

let createGet viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken ()
    let! scriptTemplates = getTemplateList ()

    let chooser =
      B.form
        token
        []
        [ B.select
            {| selectAttrs =
                [ _name_ "template"
                  Hx.trigger "input"
                  Hx.select "#page"
                  Hx.targetCss "#page"
                  Hx.post "/script/create" ]
               wrapperAttrs = [] |}
            (List.map
              (fun st -> _option [] [ _text st ])
              ("Pick a template:"
               :: (scriptTemplates |> List.sort))) ]

    let html =
      [ B.title [] "Choose a starting template to modify"
        chooser ]

    return Response.ofHtml (template "Create script" html)
  }
  |> Handler.flatten
  |> get "/script/create"

let createPost viewContext =
  handler {
    let! input =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun data -> data.TryGetStringNonEmpty "template")

    let! scriptTemplate = getTemplate input
    let! script = create scriptTemplate

    let! view = scriptManager (UserEditor script)

    let! template = viewContext.contextualTemplate
    let html = template "Edit Script" view

    return
      Response.withHxPushUrl $"/script/{script.id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/script/create"

let editGet viewContext =
  handler {
    let! id =
      Handler.fromCtx (
        Request.getRoute >> fun d -> d.GetGuid "guid"
      )

    let! existing =
      load id
      |> Handler.ofOption (
        Response.withStatusCode 404 >> Response.ofEmpty
      )

    let! view = scriptManager (UserEditor existing)
    let! template = viewContext.contextualTemplate

    let html = template "Edit Script" view

    return
      Response.withHxPushUrl $"/script/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script/{guid:guid}"

let demoGet viewContext =
  handler {
    let! demoText =
      Content.getContentText "examples/AloneInTheDark.ink"
      |> Handler.map (fun ink ->
        ink.Split '\n' |> Seq.skip 1 |> String.concat "\n")

    let demo =
      DemoEditor
        {| title = "Alone in the dark"
           ink = demoText |}

    let! view = scriptManager demo
    let! template = viewContext.contextualTemplate

    let html = template "Edit Script" view

    return
      Response.withHxPushUrl $"/script/demo"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script/demo"

let editPost viewContext =
  handler {
    let! id =
      Handler.fromCtx (
        Request.getRoute >> fun d -> d.GetGuid "guid"
      )

    let! input =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun data ->
          Option.map2
            (fun title ink ->
              {| title = title; ink = ink |})
            (data.TryGetStringNonEmpty "title")
            (data.TryGetString "ink"))

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()
    let story = compile fileHandler input.title input.ink

    let inkJson, stats =
      match story with
      | CompiledStory(story, stats) -> story.ToJson(), stats
      | CompilationErrors(_, stats) -> "{}", stats

    let! existingScript =
      load id
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let script =
      { existingScript with
          ink = input.ink
          inkJson = inkJson
          stats = stats
          title = input.title }

    do! save script

    let! template = viewContext.contextualTemplate

    let! view = scriptManager (UserEditor script)
    let html = template "Edit Script" view

    return! HxFragment "body" html
  }
  |> Handler.flatten
  |> post "/script/{guid:guid}"

let private listView scripts =
  [ B.title [] "My scripts"
    B.block
      []
      [ _a
          [ _class_ "button is-fullwidth is-primary"
            _href_ "/script/create" ]
          [ _text "Start a new script!" ] ]
    _table
      [ _class_ "table is-fullwidth" ]
      [ _thead
          []
          [ _tr
              []
              [ _th [] [ _text "Title" ]
                _th [] [ _text "Words" ]
                _th [] [ _text "Choices" ]
                _th [] [ _text "Delete?" ] ] ]
        _tbody
          []
          (scripts
           |> Seq.map (fun s ->
             _tr
               []
               [ _td
                   []
                   [ _a
                       [ _href_
                           $"/script/{s.id.ToString()}" ]
                       [ _text s.title ] ]
                 _td
                   []
                   [ _text (
                       s.stats
                       |> Option.map (fun stats ->
                         $"{stats.words}")
                       |> Option.defaultValue "??"
                     ) ]
                 _td
                   []
                   [ _text (
                       s.stats
                       |> Option.map (fun stats ->
                         $"{stats.choices}")
                       |> Option.defaultValue "??"
                     ) ]
                 _td
                   []
                   [ B.delete
                       [ Hx.delete
                           $"/script/{s.id.ToString()}"
                         Hx.targetCss "#page"
                         Hx.select "#page"
                         Hx.confirm
                           "Are you sure? There is no way to get the script back." ] ] ])
           |> List.ofSeq) ] ]

let listGet (viewContext: ViewContext) =
  handler {
    let! user = User.ensureSessionUser ()
    let! documentStore = Handler.plug<IDocumentStore, _> ()

    let session =
      documentStore.QuerySession(user.id.ToString())

    let! scripts =
      session
        .Query<Script>()
        .OrderBySql("mt_last_modified desc")
        .ToListAsync()
      |> Handler.returnTask

    let view = listView scripts
    let! template = viewContext.contextualTemplate
    let html = template "Scripts" view
    return Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script"

let editDelete viewContext =
  handler {
    let! id =
      Handler.fromCtx (
        Request.getRoute >> fun d -> d.GetGuid "guid"
      )

    let! user = User.ensureSessionUser ()
    let! documentStore = Handler.plug<IDocumentStore, _> ()

    let session =
      documentStore.LightweightSession(user.id.ToString())

    session.Delete<Script> id
    do! session.SaveChangesAsync() |> Handler.returnTask'

    let! scripts =
      session
        .Query<Script>()
        .OrderBySql("mt_last_modified desc")
        .ToListAsync()
      |> Handler.returnTask

    let view = listView scripts
    let! template = viewContext.contextualTemplate
    let html = template "Scripts" view
    return Response.ofHtml html
  }
  |> Handler.flatten
  |> delete "/script/{guid:guid}"

let createPublishedUrl script =
  handler {
    let slug = Slug.fromGuid script.id

    return!
      Handler.fromCtx (fun ctx ->
        let scheme =
          if ctx.Request.IsHttps then
            "https://"
          else
            "http://"

        $"{scheme}{ctx.Request.Host.Value}/published/{slug}")
  }

let publishPost viewContext =
  handler {
    let! scriptId =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun fd -> fd.TryGetGuid "publish")

    let! existing =
      load scriptId
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let! session = DocStore.startSession ()
    let! publishedUrl = createPublishedUrl existing

    let updated =
      { existing with
          publishedUrl = Some publishedUrl }

    session.Update updated
    do! DocStore.saveChanges session

    let! template = viewContext.contextualTemplate

    let! view = scriptManager (UserEditor updated)
    let html = template "Edit Script" view

    return! HxFragment "body" html
  }
  |> Handler.flatten
  |> post "/script/publish"

let unpublishPost viewContext =
  handler {
    let! scriptId =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun fd -> fd.TryGetGuid "unpublish")

    let! existing =
      load scriptId
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    let! session = DocStore.startSession ()

    let updated = { existing with publishedUrl = None }

    session.Update updated
    do! DocStore.saveChanges session

    let! template = viewContext.contextualTemplate

    let! view = scriptManager (UserEditor updated)
    let html = template "Edit Script" view

    return! HxFragment "body" html
  }
  |> Handler.flatten
  |> post "/script/unpublish"

type LintResponseItem =
  { line: int32
    message: string
    severity: string }

let private inkToResponse
  title
  (error: string, t: Ink.ErrorType)
  =
  let lintMessageRegex =
    Regex(
      "[A-Z]+: '"
      + Regex.Escape title
      + "' line (\d+): (.*)",
      RegexOptions.Compiled
    )

  let severity =
    match t with
    | Ink.ErrorType.Error -> "error"
    | Ink.ErrorType.Warning -> "warning"
    | _ -> "info"

  let result = lintMessageRegex.Match error

  if result.Success then
    let line = result.Groups.[1].Value |> System.Int32.Parse
    let message = result.Groups.[2].Value

    { line = line
      message = message
      severity = severity }
  else
    { line = 1
      message = error
      severity = severity }

let lintPost =
  handler {
    let! json =
      Handler.fromCtxTask
        Request.getJson<{| ink: string; title: string |}>

    let! headers = Handler.fromCtx Request.getHeaders
    let referrer = headers.GetString "Referer"

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let compileResult =
      compile fileHandler json.title json.ink

    let inkJson, stats, response =
      match compileResult with
      | CompiledStory(story, stats) ->
        story.ToJson(), stats, []
      | CompilationErrors(errors, stats) ->
        "{}",
        stats,
        errors
        |> Seq.map (inkToResponse json.title)
        |> Seq.toList

    if referrer.Length > 36 then
      match
        System.Guid.TryParse(
          referrer.Substring(referrer.Length - 36, 36)
        )
      with
      | true, id ->
        let! existingScript =
          load id
          |> Handler.ofOption (
            Response.withStatusCode 400 >> Response.ofEmpty
          )

        let script =
          { existingScript with
              ink = json.ink
              inkJson = inkJson
              stats = stats
              title = json.title }

        do! save script
      | false, _ -> do ()
    else
      do ()

    return Response.ofJson response
  }
  |> Handler.flatten
  |> post "/script/lint"

let makePlaygroundScript ink =
  handler {
    let title = "Playground"
    let fakeUserId = System.Guid()

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let id = System.Guid()
    let story = compile fileHandler title ink

    match story with
    | CompiledStory(story, stats) ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = ink
          inkJson = inkJson
          stats = stats
          title = title
          publishedUrl = None
          writerId = fakeUserId.ToString() }

      return script
    // Save the actual script even if there are errors
    // that will prevent it running
    | CompilationErrors _ ->
      return failwithf "The playground Ink didn't compile"
  }

let getExampleScript filename =
  handler {
    let fakeUserId = System.Guid()

    let! ink =
      Content.getContentText (
        System.IO.Path.Join("examples", filename)
      )

    let title =
      let firstLine = ink.Split('\n', 2).[0]

      if firstLine.StartsWith "#title " then
        firstLine.Substring 7
      else
        filename

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let id = System.Guid()
    let story = compile fileHandler title ink

    match story with
    | CompiledStory(story, stats) ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = ink
          inkJson = inkJson
          stats = stats
          title = title
          publishedUrl = None
          writerId = fakeUserId.ToString() }

      return script
    // Save the actual script even if there are errors
    // that will prevent it running
    | CompilationErrors _ ->
      return
        failwithf
          "Example file %s did not compile!"
          filename
  }

let nav =
  _a
    [ _class_ "navbar-item"; _href_ "/script" ]
    [ _text "Scripts" ]

module Service =
  open Microsoft.Extensions.DependencyInjection

  let endpoints viewContext =
    [ createGet viewContext
      createPost viewContext
      editGet viewContext
      demoGet viewContext
      editPost viewContext
      editDelete viewContext
      listGet viewContext
      publishPost viewContext
      unpublishPost viewContext
      lintPost ]

  let addService: AddService =
    fun _ sc ->
      sc.AddScoped<Ink.IFileHandler, ScriptFileHandler>()
      |> ignore

      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema
          .For<Script>()
          .MultiTenanted()
          .Metadata(fun m ->
            m.TenantId.MapTo(fun x -> x.writerId))
        |> ignore)
