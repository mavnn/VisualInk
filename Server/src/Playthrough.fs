module VisualInk.Server.Playthrough

open Marten
open Microsoft.Extensions.Logging
open Falco
open Falco.Markup
open Falco.Htmx
open Falco.Routing
open Microsoft.AspNetCore.Antiforgery
open System.Text.Json.Serialization
open System.Linq
open Prelude
open Script
open View

module B = Bulma

[<JsonFSharpConverter>]
type Choice = { index: int32; text: string }

[<JsonFSharpConverter>]
type RunAs =
  | RunAsWriter
  | RunAsPublisher of System.Guid
  | RunAsAnonymous

[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type Step =
  { gameTitle: string
    scriptId: System.Guid
    speaker: string option
    imageOverride: string option
    show: bool
    emote: string option
    music: string option
    sfx: string option
    scene: string option
    text: string
    choices: Choice array
    finished: bool
    animation: string option
    stepCount: int
    runAs: RunAs }

[<JsonFSharpConverter>]
type Steps =
  { currentStep: Step
    previousStep: Step }

[<JsonFSharpConverter>]
type PlaythroughState =
  { step: Step
    saveState: string
    previous: PlaythroughState option }

[<JsonFSharpConverter>]
[<CLIMutable>]
type PlaythroughSave =
  { Id: System.Guid
    State: PlaythroughState
    Script: Script }

[<JsonFSharpConverter>]
type PlayStart =
  { script: Script
    saveState: string
    step: Step }

[<JsonFSharpConverter>]
type PlayForward = { saveState: string; step: Step }

[<JsonFSharpConverter>]
type PlaythroughEvent =
  | PlayStart of PlayStart
  | PlayBack
  | PlayForward of PlayForward
  | PlayFromSave of PlaythroughSave

[<JsonFSharpConverter>]
[<CLIMutable>]
type Playthrough =
  { Id: System.Guid
    Script: Script
    State: PlaythroughState
    Version: int64 }

  member x.Steps =
    { currentStep = x.State.step
      previousStep =
        match x.State.previous with
        | None -> x.State.step
        | Some p -> p.step }

module Projection =
  open Marten.Events.Aggregation
  open JasperFx.Events

  type PlaythroughProjection() =
    inherit SingleStreamProjection<Playthrough, System.Guid>()

    member _.Create(playthroughEvent: IEvent<PlaythroughEvent>) : Playthrough =
      match playthroughEvent.Data with
      | PlayStart start ->
        { Id = playthroughEvent.StreamId
          Script = start.script
          Version = playthroughEvent.Version
          State =
            { step = start.step
              saveState = start.saveState
              previous = None } }
      | PlayFromSave save ->
        { Id = playthroughEvent.StreamId
          Script = save.Script
          State = save.State
          Version = playthroughEvent.Version }
      // Should never be passed to create, something's gone wrong and it won't be recoverable
      // with this stream ID
      | _ ->
        raise (
          System.ArgumentException
            "Cannot create a playthrough stream from a back or forward event"
        )

    member _.Apply
      (playthroughEvent: PlaythroughEvent, ev: IEvent, playthrough: Playthrough)
      : Playthrough =
      match playthroughEvent with
      | PlayStart _
      | PlayFromSave _ ->
        // These should never be added to an existing event stream, ignore
        { playthrough with
            Version = ev.Version }
      | PlayBack ->
        match playthrough.State.previous with
        | None ->
          // Can't go back beyond the first step, ignore
          { playthrough with
              Version = ev.Version }
        | Some previous ->
          { playthrough with
              State = previous
              Version = ev.Version }
      | PlayForward forward ->
        { playthrough with
            Version = ev.Version
            State =
              { step = forward.step
                saveState = forward.saveState
                previous = Some playthrough.State } }

let getStep previousStep (story: Ink.Runtime.Story) =
  handler {
    let getVar name bind =
      story.variablesState.[name] :?> string |> Option.ofObj |> Option.bind bind

    let getTag name =
      story.currentTags
      |> Seq.tryFind (fun t -> t.StartsWith $"{name} ")
      |> Option.map (fun t -> t.Split(' ', 2).[1])

    let speaker =
      getVar "speaker" (fun v ->
        if v = "Narrator" || v = "" then None else Some v)

    let scene = getVar "scene" (fun v -> if v = "" then None else Some v)

    let music = getVar "music" (fun v -> if v = "" then None else Some v)

    let emote = getTag "emote"

    let image = getTag "show"

    // Move to vfx for a more general term
    let aniTag = getTag "animation"
    let vfxTag = getTag "vfx"

    // Prefer the new tag if a value is provided
    let animation = if Option.isNone vfxTag then aniTag else vfxTag

    let sfx = getTag "sfx"

    let show = story.currentTags.Contains "vo" |> not

    let choices =
      story.currentChoices
      |> Seq.map (fun c -> { index = c.index; text = c.text })
      |> Seq.toArray

    return
      { previousStep with
          text = story.currentText
          speaker = speaker
          imageOverride = image
          choices = choices
          scene = scene
          show = show
          music = music
          sfx = sfx
          emote = emote
          animation = animation
          finished = not story.canContinue && choices.Length = 0
          stepCount = previousStep.stepCount + 1 }
  }

let make script =
  handler {
    let! logger = Handler.plug<ILogger<Playthrough>, _> ()

    let story = Ink.Runtime.Story script.inkJson
    story.add_onError (fun msg t -> logger.LogError(msg, t))

    story.add_onChoosePathString (fun str arr ->
      logger.LogInformation(str, arr))

    story.add_onDidContinue (fun () -> logger.LogInformation "Continued!")

    story.add_onMakeChoice (fun c ->
      logger.LogInformation("Choice made {index}!", c.index))

    story
  }

let load (guid: System.Guid) =
  handler {
    let! user = User.getSessionUser ()

    return!
      match user with
      | Some _ -> DocStore.load<Playthrough, _, _> guid
      | None -> DocStore.loadShared<Playthrough, _, _> guid
  }
  |> Handler.ofOption (Response.withStatusCode 404 >> Response.ofEmpty)

let private getRunnerSession () =
  User.getSessionUser ()
  |> Handler.bind (fun u ->
    match u with
    | Some _ -> DocStore.startSession ()
    | None -> DocStore.startSharedSession ())

let rec cont (guid: System.Guid) (expectedVersion: int64) choice =
  handler {
    let! doc = load guid
    let! story = make doc.Script
    story.state.LoadJson doc.State.saveState

    do
      match choice with
      | Some c -> story.ChooseChoiceIndex c
      | None -> ()

    story.Continue() |> ignore

    let previousStep = doc.State.step
    let! currentStep = getStep previousStep story


    // Wrapped so we definitely close the session
    // before calling `cont` recursively
    let! appendAction =
      handler {
        use! session = getRunnerSession ()

        printfn "Expected version: %A" expectedVersion

        let appendAction =
          session.Events.Append(
            doc.Id,
            expectedVersion + 1L,
            PlayForward
              { step = currentStep
                saveState = story.state.ToJson() }
            |> box
          )

        do! DocStore.saveChanges session
        return appendAction
      }

    // Skip forwards if the returned step has no content at all
    // and there is no decision to make
    if
      currentStep.choices.Length = 0
      && System.String.IsNullOrWhiteSpace currentStep.text
    then
      return! cont guid appendAction.Version None
    else
      return! load guid
  }

let private start script runAs =
  handler {
    let! story = make script

    let state = story.state.ToJson()

    let step =
      { gameTitle = script.title
        scriptId = script.id
        text = ""
        scene = None
        music = None
        sfx = None
        speaker = None
        imageOverride = None
        emote = None
        show = false
        choices = [||]
        finished = false
        animation = None
        stepCount = 0
        runAs = runAs }

    let! streamAction =
      handler {
        use! session = getRunnerSession ()

        let streamAction =
          session.Events.StartStream(
            PlayStart
              { script = script
                saveState = state
                step = step }
          )

        do! DocStore.saveChanges session
        return streamAction
      }

    let! _ = cont streamAction.Id streamAction.Version None

    return streamAction.Id
  }

let private nextLink guid expectedVersion =
  $"/playthrough/{guid.ToString()}/{expectedVersion.ToString()}"

let private backLink guid expectedVersion =
  $"/playthrough/{guid.ToString()}/{expectedVersion.ToString()}/back"

let private playthroughHxAttrs
  guid
  expectedVersion
  (token: AntiforgeryTokenSet)
  =
  [ Hx.post (nextLink guid expectedVersion)
    Hx.headers [ token.HeaderName, token.RequestToken ] ]

let private makeChoiceMenu guid expectedVersion step token =
  let choices =
    match step.finished, step.choices.Length with
    | false, 0 -> []
    | false, _ ->
      step.choices
      |> Seq.map (fun c ->
        _button
          [ yield! playthroughHxAttrs guid expectedVersion token
            _value_ (c.index.ToString())
            _name_ "choice"
            _class_ "notification is-link choiceButton" ]
          [ _text c.text ])
      |> Seq.toList
    | true, _ ->
      let href =
        match step.runAs with
        | RunAsWriter -> $"/script/{step.scriptId.ToString()}"
        | RunAsPublisher _ -> "/"
        | RunAsAnonymous -> "/script/demo"

      [ _a
          [ Hx.swapOuterHtml
            Hx.select "#page"
            Hx.targetCss "#page"
            _style_ "width: 100%;"
            _class_ "notification is-link"
            _href_ href ]
          [ _text "The End" ] ]

  _div
    [ _class_ "container is-fluid is-flex is-align-items-end has-text-centered" ]
    [ _div [ _class_ "buttons mb-5"; _style_ "width: 100%;" ] choices ]

let private makeContentBox step =
  [ match step.speaker with
    | Some c ->
      yield
        _h3
          [ _class_ "subtitle has-text-primary fade"; _id_ "character-name" ]
          [ _textEnc $"{c}:" ]
    | None -> ()
    yield _p [ _id_ "speech"; _class_ "fade" ] [ _textEnc step.text ] ]

let private makeAudio steps =
  handler {
    printfn "Current music: %A" steps.currentStep.music

    let! music =
      match steps.currentStep.finished, steps.currentStep.music with
      | false, Some music ->
        match steps.currentStep.runAs with
        | RunAsWriter -> StoryAssets.findMusic music
        | RunAsPublisher guid -> StoryAssets.findMusicAs (Some guid) music
        | RunAsAnonymous -> StoryAssets.findMusicAs None music
      | _ -> Handler.return' None

    let musicSrc =
      music
      |> Option.map (fun a -> a.url)
      |> Option.defaultValue "/1-minute-of-silence.mp3"

    let! sfx =
      match steps.currentStep.sfx with
      | Some sfx ->
        match steps.currentStep.runAs with
        | RunAsWriter -> StoryAssets.findMusic sfx
        | RunAsPublisher guid -> StoryAssets.findMusicAs (Some guid) sfx
        | RunAsAnonymous -> StoryAssets.findMusicAs None sfx
      | None -> Handler.return' None

    let sfxSrc =
      sfx
      |> Option.map (fun a -> a.url)
      |> Option.defaultValue "/1-minute-of-silence.mp3"

    return
      [ yield
          _audio
            [ _id_ "audio-background-music"
              Attr.create "hx-on::before-cleanup-element" "fe.stopMusic(this)"
              _loop_
              _src_ musicSrc ]
            []
        if Option.isSome sfx then
          yield
            _audio
              [ _id_ "audio-sfx"
                Attr.create "hx-on::before-cleanup-element" "fe.stopMusic(this)"
                _src_ sfxSrc ]
              [] ]
  }

let private stepToSpeakerImage animation step =
  let speakerStyle =
    "position: fixed; height: 60%; min-width: 5000px; object-fit: contain; bottom: 0dvh; align-self: center;"

  let nameToShow =
    match step.imageOverride with
    | None -> step.speaker
    | Some o -> Some o

  match step.show, nameToShow, step.emote with
  | false, _, _
  | _, None, _ ->
    _img [ _id_ $"img-speaker-{step.stepCount}"; _style_ "display: none;" ]
    |> Handler.return'
  | true, Some char, emote ->
    handler {
      let! speaker =
        match step.runAs with
        | RunAsPublisher guid -> StoryAssets.findSpeakerAs (Some guid) char
        | RunAsWriter -> StoryAssets.findSpeaker char
        | RunAsAnonymous -> StoryAssets.findSpeakerAs None char

      let src =
        match speaker, emote with
        | None, _ -> "/assets/speakers/shared/unknown.png"
        | Some s, None -> s.url
        | Some s, Some emote ->
          s.emotes
          |> List.tryFind (fun e -> e.emote = emote)
          |> Option.map (fun e -> e.url)
          |> Option.defaultValue s.url

      return
        _img
          [ _src_ src
            _id_ $"img-speaker-{step.stepCount}"
            _class_ animation
            _style_ speakerStyle ]
    }

let private makeSpeakerImage steps =
  handler {
    // Don't fade out if it's the same image!
    let unchanged =
      steps.previousStep.speaker = steps.currentStep.speaker
      && steps.previousStep.emote = steps.currentStep.emote

    let fadeOutStyle = if unchanged then "hide" else "fade-out"

    let entryStyle =
      [ if not unchanged then yield "fade-in" else ()
        match steps.currentStep.animation with
        | Some a -> yield a
        | None -> () ]
      |> String.concat " "

    let! thisStepImg = stepToSpeakerImage entryStyle steps.currentStep

    let! prevStepImg = stepToSpeakerImage fadeOutStyle steps.previousStep

    let speakerStyle =
      "position: fixed; height: 60%; min-width: 5000px; object-fit: contain; bottom: 0dvh; align-self: center;"

    return
      [ _div
          [ _id_ "img-speaker"; _style_ speakerStyle ]
          [ thisStepImg; prevStepImg ] ]
  }

let private makeChoiceBox guid expectedVersion step =
  handler {
    let! token = Handler.getCsrfToken ()
    let choiceMenu = makeChoiceMenu guid expectedVersion step token

    return
      _div
        [ _class_
            "is-flex is-flex-direction-column is-align-content-flex-end is-flex-grow-2"
          _id_ "choice-box" ]
        [ choiceMenu ]

  }

let private makeMessageBox guid expectedVersion step =
  handler {
    let! token = Handler.getCsrfToken ()
    let content = makeContentBox step

    _div
      [ yield _class_ "is-flex-grow-1"
        yield _style_ "z-index: 150;"
        yield!
          if step.choices.Length = 0 then
            playthroughHxAttrs guid expectedVersion token
          else
            [] ]
      [ _div
          [ _class_ "container is-fluid" ]
          [ _div
              [ _class_ "buttons is-centered are-small m-1" ]
              [ B.button
                  [ Hx.post "/playthrough/save"
                    Hx.headers [ token.HeaderName, token.RequestToken ]
                    Hx.targetCss "#hero-story"
                    Hx.select "#hero-story"
                    HxMorph.morphOuterHtml
                    _name_ "playthrough"
                    _value_ (guid.ToString()) ]
                  [ _text "Save" ] ]
            _div
              [ _class_ "box content has-text-centered mt-4"
                _style_
                  "background-color: color-mix(in oklab, var(--bulma-box-background-color), transparent 10%); "
                _id_ "content-story" ]
              content ] ]
  }

let makeContinueOverlay guid expectedVersion step =
  handler {
    let! token = Handler.getCsrfToken ()

    return
      if not step.finished && step.choices.Length = 0 then
        _div
          [ yield! playthroughHxAttrs guid expectedVersion token
            _id_ "continue-overlay"
            _style_
              "position: absolute; height: 100%; width: 100%; opacity: 0; z-index: 100;" ]
          []
      else
        _div [ _id_ "continue-overlay"; _style_ "display: none;" ] []
  }

let makeBackgroundUnderlay step =
  handler {
    let! sceneUrl =
      match step.scene with
      | Some s ->
        let nameAndTag = s.Split(" ", 2)

        match nameAndTag with
        | [| name; tag |] ->
          (match step.runAs with
           | RunAsWriter -> StoryAssets.findScene name
           | RunAsAnonymous -> StoryAssets.findSceneAs None name
           | RunAsPublisher guid -> StoryAssets.findSceneAs (Some guid) name)
          |> Handler.map (
            Option.bind (fun s -> s.tags |> List.tryFind (fun t -> t.tag = tag))
          )
          |> Handler.map (Option.map (fun t -> t.url))
        | [| name |] ->
          (match step.runAs with
           | RunAsWriter -> StoryAssets.findScene name
           | RunAsAnonymous -> StoryAssets.findSceneAs None name
           | RunAsPublisher guid -> StoryAssets.findSceneAs (Some guid) name)
          |> Handler.map (Option.map (fun s -> s.url))
        | _ -> Handler.return' None
      | None -> Handler.return' None

    let backgroundStyle =
      match sceneUrl with
      | Some bg ->
        _style_ (
          "z-index: -1; height: 100dvh; width: 100vw; background-image: url('"
          + bg
          + "'); background-size: cover; background-position: center; position: fixed;"
        )
      | None ->
        _style_ "position: fixed; z-index: -1; height: 100dvh; width: 100vw;"

    return
      _div
        [ _id_ "background-underlay"
          _class_ "has-background-light"
          backgroundStyle ]
        []
  }

let private closeButton step =
  let href =
    match step.runAs with
    | RunAsPublisher _ -> "/"
    | RunAsWriter -> $"/script/{step.scriptId.ToString()}"
    | RunAsAnonymous -> "/script/demo"

  B.delete
    [ _class_ "m-2 is-medium"
      Hx.swapOuterHtml
      Hx.select "#page"
      Hx.targetCss "#page"
      Hx.get href
      _style_ "position: absolute; top: 0; right: 0; z-index: 200;" ]

let private backButton guid expectedVersion =
  let href = backLink guid expectedVersion

  _div
    [ Hx.swapOuterHtml
      Hx.targetCss "#hero-story"
      Hx.post href
      _style_ "position: absolute; top: 0; left: 0; z-index: 200;" ]
    [ B.icon
        "arrow-left-circle"
        [ _class_ "m-2 is-medium"; _style_ "font-size: 1.8rem;" ] ]

let currentView template (playthrough: Playthrough) =
  handler {
    let! audio = makeAudio playthrough.Steps

    let! speakerImage = makeSpeakerImage playthrough.Steps

    let! choiceBox =
      makeChoiceBox playthrough.Id playthrough.Version playthrough.State.step

    let! messageBox =
      makeMessageBox playthrough.Id playthrough.Version playthrough.State.step

    let! continueOverlay =
      makeContinueOverlay
        playthrough.Id
        playthrough.Version
        playthrough.State.step

    let! backgroundUnderlay = makeBackgroundUnderlay playthrough.State.step

    return
      [ yield
          _section
            [ _class_ "hero"
              _style_ "height: 100dvh;"
              _id_ "hero-story"
              Hx.targetCss "#hero-story"
              // Attr.create "hx-on::load" "fe.requestFullscreenPlaythrough()"
              HxMorph.morphOuterHtml ]
            [ yield! audio
              yield! speakerImage
              closeButton playthrough.State.step
              backButton playthrough.Id playthrough.Version
              continueOverlay
              backgroundUnderlay
              messageBox
              choiceBox ] ]
      |> template playthrough.State.step.gameTitle
  }

let stepGet viewContext =
  handler {
    let! requestData = Handler.fromCtx Request.getRoute
    let id = requestData.GetGuid "guid"

    let! playthrough = load id

    let! html = currentView viewContext.skeletalTemplate playthrough

    return! HxFragment "hero-story" html
  }
  |> Handler.flatten
  |> get "/playthrough/{guid}"

let stepPost viewContext =
  handler {
    let! requestData = Handler.fromCtx Request.getRoute
    let id = requestData.GetGuid "guid"
    let expectedVersion = requestData.GetInt64 "expectedVersion"

    let! index =
      Handler.formDataOrFail
        (Response.withStatusCode 403 >> Response.ofEmpty)
        (fun buttonData -> buttonData.TryGetInt32 "choice" |> Some)

    let! playthrough = cont id expectedVersion index

    let! html = currentView viewContext.skeletalTemplate playthrough

    return! HxFragment "hero-story" html
  }
  |> Handler.flatten
  |> post "playthrough/{guid:guid}/{expectedVersion}"

let stepBackPost viewContext =
  handler {
    let! requestData = Handler.fromCtx Request.getRoute
    let guid = requestData.GetGuid "guid"
    let expectedVersion = requestData.GetInt64 "expectedVersion"

    use! session = getRunnerSession ()

    let streamEvent =
      session.Events.Append(guid, expectedVersion + 1L, PlayBack |> box)

    do! DocStore.saveChanges session

    let! playthrough = load guid

    let! html = currentView viewContext.skeletalTemplate playthrough

    return! HxFragment "hero-story" html
  }
  |> Handler.flatten
  |> post "playthrough/{guid:guid}/{expectedVersion}/back"

let startPost viewContext =
  handler {
    let! scriptId =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun requestData -> requestData.TryGetGuid "script")

    let! script =
      Script.load scriptId
      |> Handler.ofOption (Response.withStatusCode 404 >> Response.ofEmpty)

    let! id = start script RunAsWriter

    let! playthrough = load id

    let! html = currentView viewContext.skeletalTemplate playthrough

    return
      Response.withHxPushUrl $"/playthrough/{id}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/playthrough/start"

let savePost viewContext =
  handler {
    let! requestData =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun requestData ->
          Option.map2
            (fun playthrough name ->
              {| playthrough = playthrough
                 name = name |})
            (requestData.TryGetGuid "playthrough")
            (requestData.TryGetStringNonEmpty "name"
             |> fun n ->
               match n with
               | Some n -> Some n
               | None -> Some "Unnamed save"))

    let! playthrough = load requestData.playthrough

    let save =
      { Id = System.Guid.NewGuid()
        State = playthrough.State
        Script = playthrough.Script }

    use! session = DocStore.startSession ()
    session.Insert save
    do! DocStore.saveChanges session

    let! html = currentView viewContext.skeletalTemplate playthrough

    return
      Response.withHxPushUrl
        $"/playthrough/{requestData.playthrough}"
      >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/playthrough/save"


let startPublishedGet viewContext =
  handler {
    let! route = Handler.fromCtx Request.getRoute
    let slug = route.GetString "slug"

    let! script =
      DocStore.singleShared<Script, _> (fun q ->
        q.Where(fun s -> s.id = Slug.toGuid slug).Where(fun s -> s.AnyTenant()))
      |> Handler.ofOption (Response.withStatusCode 404 >> Response.ofEmpty)

    let! id =
      start script (script.writerId |> System.Guid.Parse |> RunAsPublisher)

    let! playthrough = load id

    let! html = currentView viewContext.skeletalTemplate playthrough

    return Response.withHxPushUrl $"/playthrough/{id}" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/published/{slug}"

let startExampleGet viewContext =
  handler {
    let! route = Handler.fromCtx Request.getRoute
    let filename = route.GetString "filename"
    let! script = getExampleScript $"{filename}.ink"

    let! id =
      start script (script.writerId |> System.Guid.Parse |> RunAsPublisher)

    let! playthrough = load id

    let! html = currentView viewContext.skeletalTemplate playthrough

    return Response.withHxPushUrl $"/playthrough/{id}" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> get "/examples/{filename}"

let startPlaygroundPost viewContext =
  handler {
    let! ink =
      Handler.formDataOrFail
        (Response.withStatusCode 400 >> Response.ofEmpty)
        (fun fd -> fd.TryGetStringNonEmpty "ink")

    let! script = makePlaygroundScript ink


    let! id = start script RunAsAnonymous

    let! playthrough = load id

    let! html = currentView viewContext.skeletalTemplate playthrough

    return Response.withHxPushUrl $"/playthrough/{id}" >> Response.ofHtml html
  }
  |> Handler.flatten
  |> post "/playground/playthrough"

module Service =
  open JasperFx.Events.Projections

  let endpoints viewContext =
    [ stepGet viewContext
      stepPost viewContext
      stepBackPost viewContext
      savePost viewContext
      startPost viewContext
      startPublishedGet viewContext
      startExampleGet viewContext
      startPlaygroundPost viewContext ]

  let addService: AddService =
    fun _ sc ->
      sc.ConfigureMarten(fun (storeOpts: StoreOptions) ->
        storeOpts.Schema.For<PlaythroughSave>().MultiTenanted() |> ignore

        storeOpts.Projections.Add<Projection.PlaythroughProjection>
          ProjectionLifecycle.Inline
        |> ignore)
