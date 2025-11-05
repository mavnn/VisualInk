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
open Microsoft.AspNetCore.Hosting

module B = Bulma

[<JsonFSharpConverter>]
type ScriptStats = { words: int; choices: int }

[<JsonFSharpConverter>]
type Script =
  { id: System.Guid
    ink: string
    inkJson: string
    stats: ScriptStats option
    title: string }

type CompilationResult =
  | CompiledStory of Ink.Runtime.Story * ScriptStats option
  | CompilationErrors of
    (string * Ink.ErrorType) seq *
    ScriptStats option

let private compile title ink =
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
        errorHandler = errorHandler
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
      Handler.plug<IWebHostEnvironment,_> ()

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
      Handler.plug<IWebHostEnvironment,_> ()

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
    use! session =
      DocStore.startSession()

    let id = System.Guid.NewGuid()
    let story = compile input.title input.ink

    match story with
    | CompiledStory(story, stats) ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = input.ink
          inkJson = inkJson
          stats = stats
          title = input.title }

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
          title = input.title }

      session.Insert<Script> script
      do! DocStore.saveChanges session
      return script
  }

let save (script: Script) =
  handler {
    use! session =
      DocStore.startSession()

    session.Update<Script> script
    do! DocStore.saveChanges session
  }

let load (guid: System.Guid) = DocStore.load<Script, System.Guid, _> guid

let editor (existing: Script) =
  handler {
    let! token = Handler.getCsrfToken()

    let form =
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
                        _value_ existing.title
                        _onkeyup_
                          "document.getElementById('run-button').setAttribute('disabled', true);"
                        _onblur_
                          "fe.callLinter(fe.getEditor());"
                        _placeholder_ "Your script title" ] ] })
          _div
            [ _hidden_; _id_ "last-saved" ]
            [ _text existing.ink ]
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
                [ _div
                    [ _class_ "control" ]
                    [ B.button
                        [ _id_ "run-button"
                          Hx.post "/playthrough/start"
                          HxMorph.morphOuterHtml
                          Hx.select "#page"
                          Hx.targetCss "#page"
                          _disabled_
                          _name_ "script"
                          _class_ "is-primary"
                          _value_ (existing.id.ToString()) ]
                        [ _text "Run" ] ] ] ] ]

    let! speakers = StoryAssets.findSpeakers()

    let encode v =
      v
      |> System.Text.Json.JsonSerializer.Serialize
      |> System.Net.WebUtility.HtmlEncode

    let speakerMenu =
      speakers
      |> List.map (fun speaker ->
        match speaker.emotes with
        | [] ->
          { B.MenuLinkItem.attr =
              [ _class_ "is-active"
                _onclick_
                  $"fe.addText({encode speaker.name})" ]
            B.MenuLinkItem.text = speaker.name }
          |> B.MenuLink
        | emotes ->
          { B.MenuSublistItem.sublabel =
              { B.MenuLinkItem.attr =
                  [ _class_ "is-active"
                    _onclick_
                      $"fe.addText({encode speaker.name})" ]
                B.MenuLinkItem.text = speaker.name }
            B.MenuSublistItem.items =
              emotes
              |> List.map (fun e ->
                { B.MenuLinkItem.attr =
                    [ _onclick_
                        $"fe.addText({encode e.emote})" ]
                  B.MenuLinkItem.text = e.emote }) }
          |> B.MenuSublist)
      |> fun list ->
          { B.MenuInput.label = "Speakers"
            B.MenuInput.items = list }

    let! scenes = StoryAssets.findScenes()

    let sceneMenu =
      scenes
      |> List.map (fun scene ->
        match scene.tags with
        | [] ->
          { B.MenuLinkItem.attr =
              [ _class_ "is-active"
                _onclick_
                  $"fe.addText({encode scene.name})" ]
            B.MenuLinkItem.text = scene.name }
          |> B.MenuLink
        | tags ->
          { B.MenuSublistItem.sublabel =
              { B.MenuLinkItem.attr =
                  [ _class_ "is-active"
                    _onclick_
                      $"fe.addText({encode scene.name})" ]
                B.MenuLinkItem.text = scene.name }
            B.MenuSublistItem.items =
              tags
              |> List.map (fun e ->
                { B.MenuLinkItem.attr =
                    [ _onclick_
                        $"fe.addText({encode e.tag})" ]
                  B.MenuLinkItem.text = e.tag }) }
          |> B.MenuSublist)
      |> fun list ->
          { B.MenuInput.label = "Scenes"
            B.MenuInput.items = list }

    let! music = StoryAssets.findAllMusic()

    let musicMenu =
      music
      |> List.collect (fun piece ->
        [ { B.MenuLinkItem.attr =
              [ _class_ "is-active"
                _onclick_
                  $"fe.addText({encode piece.name})" ]
            B.MenuLinkItem.text = piece.name }
          |> B.MenuLink ])
      |> fun list ->
          { B.MenuInput.label = "Music"
            B.MenuInput.items = list }

    let sideBar =
      B.menu [] [ speakerMenu; sceneMenu; musicMenu ]

    return
      _div
        [ _id_ "editor" ]
        [ B.columns
            [ _style_ "max-height: 70dvh;" ]
            [ B.encloseAttr
                B.column
                [ _class_ "is-three-quarters"
                  _style_ "max-height: 70dvh;" ]
                form
              B.encloseAttr
                B.column
                [ _style_
                    "overflow-y: auto; min-height: 50dvh;" ]
                sideBar ] ]
      |> List.singleton
  }

let createGet viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! token = Handler.getCsrfToken()
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

    let! view = editor script

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

    let! view = editor existing
    let! template = viewContext.contextualTemplate

    let html = template "Edit Script" view

    return
      Response.withHxPushUrl $"/script/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script/{guid:guid}"

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

    let story = compile input.title input.ink

    let inkJson, stats =
      match story with
      | CompiledStory(story, stats) -> story.ToJson(), stats
      | CompilationErrors(_, stats) -> "{}", stats

    let script =
      { id = id
        ink = input.ink
        inkJson = inkJson
        stats = stats
        title = input.title }

    do! save script

    let! template = viewContext.contextualTemplate

    let! view = editor script
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
    let! user = User.ensureSessionUser()
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

    let! user = User.ensureSessionUser()
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

    let compileResult = compile json.title json.ink

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

    do!
      if referrer.Length > 36 then
        let id =
          System.Guid(
            referrer.Substring(referrer.Length - 36, 36)
          )

        let script =
          { id = id
            ink = json.ink
            inkJson = inkJson
            stats = stats
            title = json.title }

        save script
      else
        Handler.return' ()

    return Response.ofJson response
  }
  |> Handler.flatten
  |> post "/script/lint"

let nav =
  _a
    [ _class_ "navbar-item"; _href_ "/script" ]
    [ _text "Scripts" ]

module Service =

  let endpoints viewContext =
    [ createGet viewContext
      createPost viewContext
      editGet viewContext
      editPost viewContext
      editDelete viewContext
      listGet viewContext
      lintPost ]

  let addService: AddService =
    fun _ sc ->
      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema.For<Script>().MultiTenanted()
        |> ignore)
