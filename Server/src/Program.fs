module VisualInk.Server.Program

open Falco
open Falco.Markup.Html
open Falco.Htmx
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Serilog
open Serilog.Formatting.Compact
open Marten
open VisualInk.Server
open Prelude
open Serilog.Events
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.Http
open Falco.Markup
open System.Security.Claims

module B = Bulma

let connectionString =
  let postgresPort =
    System.Environment.GetEnvironmentVariable "PGPORT"

  let postgresHost =
    System.Environment.GetEnvironmentVariable "PGHOST"

  let postgresUser =
    let envUser =
      System.Environment.GetEnvironmentVariable "PGUSER"

    if isNull envUser then "postgres" else envUser

  let postgresDB =
    let envUser =
      System.Environment.GetEnvironmentVariable "PGDATABASE"

    if isNull envUser then "postgres" else envUser

  let postgresPassword =
    let envPassword =
      System.Environment.GetEnvironmentVariable "PGPASSWORD"

    if isNull envPassword then
      ""
    else
      $";password={envPassword}"

  match postgresPort, postgresHost with
  | null, _
  | _, null ->
    raise
    <| new System.Exception(
      "You must supply a port and host to connect to postgres"
    )

  | port, host ->
    $"Host={host};Port={port};Database={postgresDB};Username={postgresUser}{postgresPassword}"

let skeletalTemplate title content =
  _html
    [ _lang_ "en" ]
    [ _head
        []
        [ _title
            [ Hx.swapOob OuterHTML ]
            [ _text (title + " | Visual Ink") ]
          _meta [ _charset_ "utf-8" ]
          _meta
            [ _name_ "viewport"
              _content_
                "width=device-width, initial-scale=1" ]
          _meta
            [ _name_ "htmx-config"
              _content_
                """{&quot;requestClass&quot;:&quot;is-loading&quot;}""" ]
          _link
            [ _rel_ "icon"
              _type_ "image/png"
              _href_ "/favicon-32x32.png"
              _size_ "32x32" ]
          _link
            [ _rel_ "icon"
              _type_ "image/png"
              _href_ "/favicon-192x192.png"
              _size_ "192x192" ]
          _link [ _rel_ "stylesheet"; _href_ "/style.css" ]
          _link
            [ _rel_ "stylesheet"
              _href_
                "https://cdn.jsdelivr.net/npm/@mdi/font@7.4.47/css/materialdesignicons.min.css" ]
          _script [ _src_ "/bundle.js" ] [] ]
      _body
        [ _id_ "body"; Hx.ext "morph"; Hx.boostOn ]
        [ _div
            [ _id_ "page"
              _class_ "is-flex is-flex-direction-column"
              _style_ "min-height: 100vh;" ]
            content ] ]

let makeNavbar: Handler<XmlNode, HttpHandler> =
  handler {
    let! userView = User.navbarAccountView ()
    let! scriptNav = Script.nav

    return
      B.navbar
        [ _id_ "navbar" ]
        { brand =
            [ B.navbarItemA
                [ _href_ "/"; _id_ "site-logo" ]
                [ Logo.svg ] ]
          menu =
            [ B.navbarStart
                []
                [ scriptNav
                  StoryAssets.speakerNav
                  StoryAssets.sceneNav
                  StoryAssets.musicNav ]
              B.navbarEnd [] (InfoPages.nav :: userView) ] }
  }


let intro =
  """
      Visual Ink is a tool for building, playing, and publishing visual novels aimed
      at first time writers and quick prototyping.

      Write straight forward scripts using [Ink](https://www.inklestudios.com/ink/)
      to create your own visual novels, with characters, sound tracks, locations...
      and choices!

      Want a taste? Try our short example game [Alone In The Dark](/examples/AloneInTheDark)
      and then have a look at [the script](/script/demo) that produced the game you just played!

      If you want to try writing your own novels, you'll need to [sign up](/user/signup) to
      create an account so you can save and run your own scripts.

      For a more guided approach, we're also running courses aimed at people nine years old
      and up who are interested in learning more about story telling and coding in
      partnership with [Thinkers Meetup](https://www.thinkersmeetup.com/). Have a look for
      the Coding Games with a Story course for your age group, which run once per half term.
  """
  |> (fun s -> s.Split '\n')
  |> Array.map (fun line ->
    line.Substring(min 6 line.Length))
  |> String.concat "\n"
  |> Markdig.Markdown.ToHtml

let contextualTemplate: ContextualTemplate =
  handler {
    let! navbar = makeNavbar
    let! footer = InfoPages.footer

    return
      fun title content ->
        skeletalTemplate
          title
          [ navbar
            B.section
              [ _class_ "is-flex-grow-1" ]
              [ B.container [] content ]
            footer ]
  }

let indexGet =
  handler {
    let! template = contextualTemplate

    let! exampleInk =
      Content.getContentText "examples/AloneInTheDark.ink"
      // Skip the title line for the front page example
      |> Handler.map (fun s -> s.Split('\n', 2).[1])

    let html =
      [ B.title [] "Welcome to Visual Ink!"
        B.columns
          []
          [ B.column
              [ _class_ "is-half" ]
              [ B.content [] [ _text intro ] ]
            B.column
              [ _class_ "is-half" ]
              [

                B.image
                  [ _class_ "mb-2"
                    _style_
                      "width: 100%; object-fill: none;" ]
                  [ _src_ "/example.png"
                    _style_
                      "border-radius: var(--bulma-radius-medium);"
                    _alt_
                      "Picture of a visual novel play through in progress with a cartoon character saying 'Once upon a time'" ]
                B.block
                  [ _id_ "frontpage-example" ]
                  [ Elem.create
                      "ink-element"
                      []
                      [ _pre
                          []
                          [ _code [] [ _text exampleInk ] ] ] ] ] ] ]
      |> B.container []
      |> List.singleton
      |> template "Visual Ink"

    return
      Response.withHxPushUrl "/"
      >> Response.withHxRetarget (Css "#page")
      >> Response.withHxReselect "#page"
      >> Response.withHxReswap OuterHTML
      >> Response.ofHtml html
  }
  |> Handler.flatten

let viewContext: View.ViewContext =
  { navbar = makeNavbar
    contextualTemplate = contextualTemplate
    skeletalTemplate = skeletalTemplate
    notFound =
      Response.withStatusCode 404 >> Response.ofEmpty }

let endpoints =
  List.concat
    [ [ get "/" indexGet ]
      Playthrough.Service.endpoints viewContext
      Script.Service.endpoints viewContext
      User.Service.endpoints viewContext
      StoryAssets.Service.endpoints viewContext
      InfoPages.Service.endpoints viewContext ]

type UserIdEnricher
  (httpContextAccessor: IHttpContextAccessor) =
  interface Core.ILogEventEnricher with
    member _.Enrich(logEvent, propertyFactory) =
      let ctx = httpContextAccessor.HttpContext

      match ctx.User with
      | null -> ()
      | principal ->
        match
          System.Guid.TryParse(
            principal.FindFirstValue "userId"
          )
        with
        | true, guid ->
          logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("UserId", guid)
          )
        | false, _ -> ()

[<EntryPoint>]
let main _ =
  let makeLogConfig (lc: LoggerConfiguration) =
    let logConfig =
      lc.MinimumLevel
        .Override(
          "Microsoft.AspNetCore",
          LogEventLevel.Warning
        )
        .Enrich.FromLogContext()

    if
      System.Environment.GetEnvironmentVariable
        "SERILOG_JSON_LOGGING" = "true"
    then
      logConfig.WriteTo.Console(
        RenderedCompactJsonFormatter()
      )
      |> ignore
    else
      logConfig.WriteTo.Console(
        outputTemplate =
          "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} U:{UserId}{NewLine}{Exception}"
      )
      |> ignore

    logConfig

  Log.Logger <-
    makeLogConfig(LoggerConfiguration()).CreateLogger()

  let configureMarten: System.Action<StoreOptions> =
    System.Action<StoreOptions>
      (fun (storeOpts: StoreOptions) ->
        storeOpts.Connection connectionString

        storeOpts.UseSystemTextJsonForSerialization())

  let builder =
    WebApplication
      .CreateBuilder()
      .AddServices(fun _ ->
        HttpServiceCollectionExtensions.AddHttpContextAccessor)
      .AddServices(fun _ sc ->
        SignalRDependencyInjectionExtensions
          .AddSignalR(sc)
          .Services)
      .AddServices(fun _ sc ->
        sc.AddSingleton<UserIdEnricher>())
      .AddServices(fun _ sc ->
        SerilogServiceCollectionExtensions.AddSerilog(
          sc,
          fun sp lc ->
            let enricher = sp.GetService<UserIdEnricher>()

            (makeLogConfig lc).Enrich.With enricher
            |> ignore
        ))
      .AddServices(fun _ sc ->
        MartenServiceCollectionExtensions
          .AddMarten(sc, configureMarten)
          .UseLightweightSessions()
        |> ignore

        sc)
      .AddServices(Playthrough.Service.addService)
      .AddServices(User.Service.addService)
      .AddServices(Script.Service.addService)
      .AddServices(StoryAssets.Service.addService)
      .AddServices(InfoPages.Service.addService)
      .AddServices(fun _ ->
        AntiforgeryServiceCollectionExtensions.AddAntiforgery)
      .AddServices(fun _ sc -> sc.AddAuthorization())
      .AddServices(fun _ sc ->
        AuthenticationServiceCollectionExtensions
          .AddAuthentication(sc)
          .AddCookie()
        |> ignore

        sc)
      .AddServices(fun _ sc ->
        sc.Configure<FormOptions>
          (fun (opts: FormOptions) ->
            opts.BufferBody <- true
            // 5mb file limit
            opts.MultipartBodyLengthLimit <-
              5L * 1024L * 1024L))

  builder
    .Build()
    .Use(
      SerilogApplicationBuilderExtensions.UseSerilogRequestLogging
    )
    .Use(StaticFileExtensions.UseStaticFiles)
    .UseRouting()
    .Use(fun (builder: IApplicationBuilder) ->
      builder.UseEndpoints(fun endpointBuilder ->
        endpointBuilder.MapHub<CollabHub.CollabHub>
          "/collab"
        |> ignore)
    )
    .UseFalco(endpoints)
    .Run(
      Response.withStatusCode 404
      >> Response.ofPlainText "Not found"
    )

  Log.CloseAndFlush()

  0
