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
open System.Text.Json.Serialization
open Serilog.Events
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.Http

module B = Bulma

module Logo =
  open Falco.Markup.Svg.Elem
  open Falco.Markup.Svg

  let svg =
    _svg
      [ Attr.viewBox "0 0 50 50"
        _height_ "50mm"
        _width_ "50mm" ]
      [ _style
          []
          [ _text
              ".pen { stroke:var(--bulma-navbar-item-color);stroke-width:0.7;stroke-dasharray:none;fill:none; }\n"
            _text
              ".star { fill:var(--bulma-navbar-item-color);stroke:var(--bulma-navbar-item-color);stroke-width:0.7;stroke-dasharray:none; }\n" ]
        g
          [ _id_ "g10"
            Attr.transform
              "matrix(1.2590477,0,0,1.2590477,-3.4741765,1.896902)" ]
          [ path
              [ _id_ "path3"
                _class_ "pen"
                Attr.d
                  "m 19.737261,24.678805 a 9.2339268,9.2339268 0 0 1 -2.432202,-6.245154 9.2339268,9.2339268 0 0 1 0.360587,-2.555238" ]
              []
            path
              [ _id_ "path9"
                _class_ "pen"
                Attr.d
                  "m 34.335923,13.486613 a 9.2339268,9.2339268 0 0 1 1.43699,4.947038 v 0 a 9.2339268,9.2339268 0 0 1 -0.985659,4.151081" ]
              []
            path
              [ _id_ "path1"
                _class_ "pen"
                Attr.d
                  "m 3.4541724,20.827633 c 0,0 1.744186,2.359781 5.6771545,2.051984 3.9329681,-0.307798 9.9521201,-11.593707 17.3734621,-11.525308 7.42134,0.0684 15.321475,7.079343 15.321475,7.079343 l -0.102597,-0.0684" ]
              []
            path
              [ _id_ "path2"
                _class_ "pen"
                Attr.d
                  "m 12.893297,22.571819 c 0,0 5.369357,2.599179 12.893296,2.770178 7.52394,0.170999 13.474692,-6.361149 13.474692,-6.361149 l -0.0684,-0.102599" ]
              [] ]
        path
          [ _id_ "path10"
            _class_ "star"
            Attr.d
              "m 36.350514,37.631083 c -0.190451,0.41896 -3.994971,-2.674946 -4.516718,-2.557122 -0.521746,0.117823 -2.599581,3.941003 -3.065821,4.018588 -0.46624,0.07758 0.526558,-3.390191 0.248764,-3.716904 -0.277793,-0.326713 -4.79282,1.600191 -4.858814,1.051065 -0.06599,-0.549125 2.420452,-4.460102 2.566377,-4.917763 0.145924,-0.457662 -1.973821,-3.27899 -1.54933,-3.508416 0.42449,-0.229426 4.901671,2.534557 5.406583,2.527566 0.504911,-0.007 1.947072,-3.101405 2.362548,-2.774336 0.415477,0.327069 -0.84883,2.197047 -0.435357,2.502209 0.413473,0.305162 4.733268,0.221372 4.801744,0.704523 0.06848,0.483151 -2.274629,2.528955 -2.379728,3.053551 -0.105099,0.524595 1.610203,3.19808 1.419752,3.617039 z"
            Attr.transform
              "matrix(1.0989269,-0.48067842,0.48067842,1.0989269,-19.501454,3.277324)" ]
          [] ]

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
          _link
            [ _rel_ "stylesheet"
              _href_
                "/style.css" ]
          _link
            [ _rel_ "stylesheet"
              _href_
                "https://cdn.jsdelivr.net/npm/@mdi/font@7.4.47/css/materialdesignicons.min.css" ]
          _script [ _src_ "/bundle.js" ] []
           ]
      _body
        [ _id_ "body"; Hx.ext "morph"; Hx.boostOn ]
        [ _div
            [ _id_ "page"
              _class_ "is-flex is-flex-direction-column"
              _style_ "min-height: 100vh;" ]
            content ] ]

let makeNavbar =
  handler {
    let! userView = User.navbarAccountView

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
                [ Script.nav
                  StoryAssets.speakerNav
                  StoryAssets.sceneNav
                  StoryAssets.musicNav ]
              B.navbarEnd [] (InfoPages.nav :: userView) ] }
  }


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

    let html =
      [ B.title [] "Welcome to Visual Ink!"
        B.columns
          []
          [ B.column
              [ _class_ "is-half" ]
              [ B.content
                  []
                  [ _p'
                      "Visual Ink is a fun tool for building and playing visual novels."
                    _p
                      []
                      [ _text "It uses "
                        _a
                          [ _href_
                              "https://www.inklestudios.com/ink/"
                            Hx.boostOff ]
                          [ _text "Ink" ]
                        _text
                          " from Inkle Studios to let you write your visual novel as if it were a movie script." ]
                    _p
                      []
                      [ _text
                          "Not sure how to get started? Well, you'll need to "
                        _a
                          [ _href_ "/user/signup" ]
                          [ _text "sign up" ]
                        _text
                          " if it is your first time here." ]
                    _p'
                      "After that, try the 'Scripts' button in the menu at the top, and create your first script. Or if you're in a class or club, I'm sure the person who's running it will have something ready for you!" ] ]
            B.column
              [ _class_ "is-half" ]
              [

                B.image
                  [ _class_ "mb-2"
                    _style_
                      "width: 100%; object-fill: none;" ]
                  [ _src_ "/example.png"
                    _style_ "border-radius: var(--bulma-radius-medium);"
                    _alt_
                      "Picture of a visual novel play through in progress with a cartoon character saying 'Once upon a time'" ]
                B.block [] [
                   B.box [] [
                       _text 
                         """
VAR speaker = "Narrator"
VAR scene = ""
VAR music = ""

~scene = "Cafe"

The smell of coffee filled the air.

~music = "Background Jazz"
~speaker = "Eddy"

Once upon a time...
                         """ |> B.enclose _pre

                   ]
                ]] ] ]
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
      let user = User.getSessionUserFromCtx ctx

      match user with
      | Some u ->
        logEvent.AddPropertyIfAbsent(
          propertyFactory.CreateProperty("UserId", u.id)
        )
      | None -> ()

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

        storeOpts.UseSystemTextJsonForSerialization(
          configure =
            fun opts ->
              JsonFSharpOptions
                .Default()
                .WithSkippableOptionFields()
                .AddToJsonSerializerOptions
                opts
        ))

  let builder =
    WebApplication
      .CreateBuilder()
      .AddServices(fun _ ->
        HttpServiceCollectionExtensions.AddHttpContextAccessor)
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
            opts.MultipartBodyLengthLimit <- 5L * 1024L * 1024L))

  builder
    .Build()
    .Use(StaticFileExtensions.UseStaticFiles)
    .Use(
      SerilogApplicationBuilderExtensions.UseSerilogRequestLogging
    )
    .UseRouting()
    .UseFalco(endpoints)
    .Run(
      Response.withStatusCode 404
      >> Response.ofPlainText "Not found"
    )

  Log.CloseAndFlush()

  0
