module VisualInk.Server.Script

open Marten
open Falco
open Falco.Markup
open Falco.Htmx
open Prelude
open Falco.Routing
open View
open VisualInkPlugin
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

type CompileCompleted =
  { compiled: Ink.Runtime.Story
    parsed: Ink.Parsed.Story
    stats: ScriptStats }

type CompilationResult =
  { completed: CompileCompleted option
    errors: (string * Ink.ErrorType) seq }

type ScriptFileHandler(docStore: IDocumentStore, userService: User.IUserService)
  =
  interface Ink.IFileHandler with
    member _.ResolveInkFilename includeName = includeName

    member _.LoadInkFileContents(fullFilename: string) : string =
      match isPluginInclude fullFilename with
      | Some pluginInput ->
        generatePluginInclude pluginInput
      | None ->
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
        | None -> failwithf "Script '%s' not found" fullFilename

type ErrorList = System.Collections.Generic.List<string * Ink.ErrorType>

let private makeCompiler ink title (errors: ErrorList) fileHandler =
  let errorHandler: Ink.ErrorHandler =
    Ink.ErrorHandler(fun err t -> errors.Add(err, t))

  new Ink.Compiler(
    ink,
    new Ink.Compiler.Options(
      sourceFilename = title,
      errorHandler = errorHandler,
      fileHandler = fileHandler,
      plugins = System.Collections.Generic.List [VisualInkPlugin() :> Ink.IPlugin]
    )
  )

let private compile fileHandler title ink =
  let errors = new System.Collections.Generic.List<string * Ink.ErrorType>()

  let compiler = makeCompiler ink title errors fileHandler

  let parsed = compiler.Parse()
  let story = compiler.Compile()

  match Option.ofObj parsed, Option.ofObj story with
  | Some parsed, Some compiled ->
    let stats =
      Ink.Stats.Generate parsed
      |> fun s -> { words = s.words; choices = s.choices }

    { completed =
        Some
          { compiled = compiled
            parsed = parsed
            stats = stats }
      errors = errors }
  | _ -> { completed = None; errors = errors }

type ScriptTemplate = { title: string; ink: string }

let private getTemplateList () =
  handler {
    let! hostEnvironment = Handler.plug<IWebHostEnvironment, _> ()

    let contentRootPath = hostEnvironment.WebRootPath

    return
      System.IO.Path.Join(contentRootPath, "assets", "shared", "templates")
      |> fun path ->
        System.IO.Directory.EnumerateFiles(path, "*.ink")
        |> Seq.map System.IO.Path.GetFileNameWithoutExtension
        |> Seq.toList
  }

let private getTemplate title =
  handler {
    let! hostEnvironment = Handler.plug<IWebHostEnvironment, _> ()

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

    match story.completed with
    | Some { compiled = story; stats = stats } ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = input.ink
          inkJson = inkJson
          stats = Some stats
          title = input.title
          publishedUrl = None
          writerId = user.id.ToString() }

      session.Insert<Script> script
      do! session.SaveChangesAsync() |> Handler.returnTask'
      return script
    // Save the actual script even if there are errors
    // that will prevent it running
    | None ->
      let script =
        { id = id
          ink = input.ink
          inkJson = "{}"
          stats = None
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

    return
      Elem.create
        "ink-editor"
        [ yield _id_ "codemirror"
          yield _class_ "control"
          yield _style_ "height: 100%;"
          yield _title_ title
          yield Attr.create "token-value" token.RequestToken
          yield Attr.create "token-header" token.HeaderName
          match input with
          | DemoEditor _ -> ()
          | UserEditor existing ->
            yield Attr.create "script-id" (existing.id.ToString())

            match existing.publishedUrl with
            | Some url -> yield Attr.create "published-url" url
            | None -> () ]
        [ _pre [] [ _text ink ] ]

  }

let scriptManager (editorInput: EditorInput) =
  handler {
    let! form = editor editorInput

    let! speakers = StoryAssets.findSpeakers ()

    let! scenes = StoryAssets.findScenes ()

    let! music = StoryAssets.findAllMusic ()

    let speakerJson = _div [_id_ "speaker-json"; _hidden_] [
       _text (serializeJson speakers)
    ]

    let musicJson = _div [_id_ "music-json"; _hidden_ ] [
       _text (serializeJson music)
    ]

    let sceneJson = _div [_id_ "scene-json"; _hidden_ ] [
       _text (serializeJson scenes)
    ]

    return
      [ _div [ _id_ "editor" ] [ form ]
        speakerJson
        musicJson
        sceneJson ]
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
              ("Pick a template:" :: (scriptTemplates |> List.sort))) ]

    let html = [ B.title [] "Choose a starting template to modify"; chooser ]

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

    return Response.withHxPushUrl $"/script/{script.id}" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/script/create"

let editGet viewContext =
  handler {
    let! id = Handler.fromCtx (Request.getRoute >> fun d -> d.GetGuid "guid")

    let! existing =
      load id
      |> Handler.ofOption (Response.withStatusCode 404 >> Response.ofEmpty)

    let! view = scriptManager (UserEditor existing)
    let! template = viewContext.contextualTemplate

    let html = template "Edit Script" view

    return Response.withHxPushUrl $"/script/{id}" >> Response.ofHtml html
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

    return Response.withHxPushUrl $"/script/demo" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script/demo"

let collabGet viewContext =
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

    return Response.withHxPushUrl $"/script/demo" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/script-collab/{groupName}"

let editPost viewContext =
  handler {
    let! id = Handler.fromCtx (Request.getRoute >> fun d -> d.GetGuid "guid")

    let! input =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun data ->
          Option.map2
            (fun title ink -> {| title = title; ink = ink |})
            (data.TryGetStringNonEmpty "title")
            (data.TryGetString "ink"))

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()
    let story = compile fileHandler input.title input.ink

    let inkJson, stats =
      match story.completed with
      | Some { compiled = story; stats = stats } -> story.ToJson(), Some stats
      | None -> "{}", None

    let! existingScript =
      load id
      |> Handler.ofOption (Response.withStatusCode 400 >> Response.ofEmpty)

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
          [ _class_ "button is-fullwidth is-primary"; _href_ "/script/create" ]
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
                _th [] [ _text "Published" ]
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
                       [ _href_ $"/script/{s.id.ToString()}" ]
                       [ _text s.title ] ]
                 _td
                   []
                   [ _text (
                       s.stats
                       |> Option.map (fun stats -> $"{stats.words}")
                       |> Option.defaultValue "??"
                     ) ]
                 _td
                   []
                   [ _text (
                       s.stats
                       |> Option.map (fun stats -> $"{stats.choices}")
                       |> Option.defaultValue "??"
                     ) ]
                 _td
                   []
                   [ B.iconText
                       []
                       [ if Option.isSome s.publishedUrl then
                           B.icon
                             "check-outline"
                             [ _class_ "has-text-primary" ] ] ]
                 _td
                   []
                   [ B.delete
                       [ Hx.delete $"/script/{s.id.ToString()}"
                         Hx.targetCss "#page"
                         Hx.select "#page"
                         Hx.confirm
                           "Are you sure? There is no way to get the script back." ] ] ])
           |> List.ofSeq) ] ]

let listGet (viewContext: ViewContext) =
  handler {
    let! user = User.ensureSessionUser ()
    let! documentStore = Handler.plug<IDocumentStore, _> ()

    let session = documentStore.QuerySession(user.id.ToString())

    let! scripts =
      session.Query<Script>().OrderBySql("mt_last_modified desc").ToListAsync()
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
    let! id = Handler.fromCtx (Request.getRoute >> fun d -> d.GetGuid "guid")

    let! user = User.ensureSessionUser ()
    let! documentStore = Handler.plug<IDocumentStore, _> ()

    let session = documentStore.LightweightSession(user.id.ToString())

    session.Delete<Script> id
    do! session.SaveChangesAsync() |> Handler.returnTask'

    let! scripts =
      session.Query<Script>().OrderBySql("mt_last_modified desc").ToListAsync()
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
          if ctx.Request.Host.Value.Contains "localhost" then
            "http://"
          else
            "https://"

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
      |> Handler.ofOption (Response.withStatusCode 400 >> Response.ofEmpty)

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
      |> Handler.ofOption (Response.withStatusCode 400 >> Response.ofEmpty)

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

let private inkToResponse title (error: string, t: Ink.ErrorType) =
  let lintMessageRegex =
    Regex(
      "[A-Z]+: '" + Regex.Escape title + "' line (\d+): (.*)",
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

type AutocompleteContent =
  { lists: string list
    globalVariables: string list }

let private getAutocompleteContext (story: Ink.Parsed.Story) =
  story.ResolveWeavePointNaming()
  let globalVariables =
    story.FindAll<Ink.Parsed.VariableAssignment>()
    |> Seq.map (fun v -> v.variableName)
    |> Set.ofSeq
    |> List.ofSeq


  // story.FindAll<Ink.Parsed.Identifier>()
  // |> Seq.map (fun v -> v.ToString())
  // |> Set.ofSeq
  // |> printfn "%A"

  { lists = []; globalVariables = globalVariables }

let lintPost =
  handler {
    let! json =
      Handler.fromCtxTask Request.getJson<{| ink: string; title: string |}>

    let! headers = Handler.fromCtx Request.getHeaders
    let referrer = headers.GetString "Referer"

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let compileResult = compile fileHandler json.title json.ink

    let lines = compileResult.errors |> Seq.map (inkToResponse json.title)

    let story, stats =
      match compileResult.completed with
      | Some result -> result.compiled.ToJson(), Some result.stats
      | None -> "{}", None

    let autocompleteContext =
      compileResult.completed
      |> Option.map (fun { parsed = parsed } -> getAutocompleteContext parsed)
      |> Option.defaultValue { lists = []; globalVariables = ["speaker";"scene";"music"]}

    if referrer.Length > 36 then
      match
        System.Guid.TryParse(referrer.Substring(referrer.Length - 36, 36))
      with
      | true, id ->
        let! existingScript =
          load id
          |> Handler.ofOption (Response.withStatusCode 400 >> Response.ofEmpty)

        let script =
          { existingScript with
              ink = json.ink
              inkJson = story
              stats = stats
              title = json.title }

        do! save script
      | false, _ -> do ()
    else
      do ()

    return Response.ofJson {|lines = lines; autocompleteContext = autocompleteContext |}
  }
  |> Handler.flatten
  |> post "/script/lint"

let makePlaygroundScript ink =
  handler {
    let title = "Playground"
    let fakeUserId = System.Guid()

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let id = System.Guid()
    let compileResult = compile fileHandler title ink

    match compileResult.completed with
    | Some { compiled = story; stats = stats } ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = ink
          inkJson = inkJson
          stats = Some stats
          title = title
          publishedUrl = None
          writerId = fakeUserId.ToString() }

      return script
    | None -> return failwithf "The playground Ink didn't compile"
  }

let getExampleScript filename =
  handler {
    let fakeUserId = System.Guid()

    let! ink =
      Content.getContentText (System.IO.Path.Join("examples", filename))

    let title =
      let firstLine = ink.Split('\n', 2).[0]

      if firstLine.StartsWith "#title " then
        firstLine.Substring 7
      else
        filename

    let! fileHandler = Handler.plug<Ink.IFileHandler, _> ()

    let id = System.Guid()
    let compileResult = compile fileHandler title ink

    match compileResult.completed with
    | Some { compiled = story; stats = stats } ->
      let inkJson = story.ToJson()

      let script =
        { id = id
          ink = ink
          inkJson = inkJson
          stats = Some stats
          title = title
          publishedUrl = None
          writerId = fakeUserId.ToString() }

      return script
    | None -> return failwithf "Example file %s did not compile!" filename
  }

let private getInkToolsStack viewContext =
    handler {
      let! path = Handler.fromCtx Request.getRoute
      let name = path.GetStringNonEmpty "name"
      let sizeStr = path.GetStringNonEmpty "size"
      let nil = path.GetStringNonEmpty "nil"
      match System.Int32.TryParse sizeStr with
      | true, size ->
        let! template = viewContext.contextualTemplate
        let view =      
          Elem.create "ink-element" [] [_pre [] [_code [] [ _text (generatePluginInclude (StackInclude (name, size, nil)))]]]
        return Response.ofHtml (template "Generated stack include" [view])
        
      | false, _ ->
        return Response.withStatusCode 400 >> Response.ofEmpty
    }
    |> Handler.flatten
    |> get "/inkTools/{name}/{size}/{nil}"

let nav =
  handler {

    match! User.getSessionUser () with
    | Some _ ->
      _a [ _class_ "navbar-item"; _href_ "/script" ] [ _text "Scripts" ]
    | None ->
      _a
        [ _class_ "navbar-item"; _href_ "/script/demo" ]
        [ _text "Demo Editor" ]
  }

module Service =
  open Microsoft.Extensions.DependencyInjection

  let endpoints viewContext =
    [ createGet viewContext
      createPost viewContext
      editGet viewContext
      demoGet viewContext
      collabGet viewContext
      editPost viewContext
      editDelete viewContext
      listGet viewContext
      publishPost viewContext
      unpublishPost viewContext
      getInkToolsStack viewContext
      lintPost ]

  let addService: AddService =
    fun _ sc ->
      sc.AddScoped<Ink.IFileHandler, ScriptFileHandler>() |> ignore

      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema
          .For<Script>()
          .MultiTenanted()
          .Metadata(fun m -> m.TenantId.MapTo(fun x -> x.writerId))
        |> ignore)
