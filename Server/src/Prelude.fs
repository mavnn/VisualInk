module VisualInk.Server.Prelude

open System.Threading.Tasks
open Falco
open Falco.Security
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Hosting

(*
       HANDLERS
       A set of helpers for writing http handlers in Falco with
       a computational expression, and some helpers for Htmx
*)

type Handler<'a> =
  HttpContext -> Task<HttpContext * Result<'a, HttpHandler>>

module Handler =

  let inline return' x : Handler<_> =
    fun ctx -> Task.FromResult(ctx, Ok x)

  let returnTask x : Handler<_> =
    fun ctx ->
      task {
        let! v = x
        return ctx, Ok v
      }

  let returnTask' (x: Task) : Handler<_> =
    fun ctx ->
      task {
        let! v = x
        return ctx, Ok v
      }

  let inline bind
    ([<InlineIfLambda>] f: 'a -> Handler<'b>)
    ([<InlineIfLambda>] x: Handler<'a>)
    : Handler<'b> =
    fun ctx ->
      task {
        let! newCtx, soFar = x ctx

        match soFar with
        | Ok v -> return! f v newCtx
        | Error err -> return newCtx, Error err
      }

  let map f x = bind (fun v -> return' (f v)) x

  let collect f xs =
    Seq.fold
      (fun acc next ->
        acc
        |> bind (fun results ->
          f next |> map (fun result -> result :: results)))
      (return' [])
      xs
    |> map List.rev

  let getCtx: Handler<HttpContext> =
    fun ctx -> Task.FromResult(ctx, Ok ctx)

  let updateCtx f : Handler<unit> =
    fun ctx -> Task.FromResult(f ctx, Ok())

  let fromCtx f : Handler<_> = getCtx |> bind (f >> return')

  let fromCtxTask f : Handler<_> =
    getCtx |> bind (f >> returnTask)

  let plug<'Interface> () : Handler<'Interface> =
    fromCtx (fun ctx -> ctx.Plug<'Interface>())

  let failure errHandler : Handler<_> =
    fun ctx -> Task.FromResult(ctx, Error errHandler)

  let getCsrfToken
    : Handler<
        Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet
       > =
    fromCtx Xsrf.getToken

  let formDataOrFail onFailure mapForm : Handler<_> =
    fromCtxTask Request.getFormSecure
    |> bind (fun maybeFormCollection ->
      match maybeFormCollection with
      | Some formCollection ->
        match mapForm formCollection with
        | Some v -> return' v
        | None -> failure onFailure
      | None -> failure onFailure)

  let ofOption onFailure value =
    value
    |> bind (fun v ->
      match v with
      | Some v -> return' v
      | None -> failure onFailure)

  let flatten
    (handler: Handler<HttpHandler>)
    : HttpHandler =
    fun ctx ->
      task {
        match! handler ctx with
        | ctx', Ok success -> return! success ctx'
        | ctx', Error failure -> return! failure ctx'
      }

  let applyIn (f: Handler<'a -> 'b>) (v: 'a) =
    map (fun f' -> f' v) f

  let tap f =
      bind (fun v -> map (fun () -> v) (f v))

  let getHrefForFilePath filePath =
    plug<IWebHostEnvironment> ()
    |> map (fun h ->
      "/" + System.IO.Path.GetRelativePath(
        h.WebRootPath,
        filePath
      ))

  let createAbsoluteLink path =
    fromCtx (fun ctx ->
      let host = ctx.Request.Host.Value
      let scheme = if host.StartsWith "localhost" then "http://" else "https://"
      $"{scheme}{host}{path}")


type HandlerBuilder() =
  member _.Return a = Handler.return' a
  member _.ReturnFrom(a: Handler<_>) = a
  member _.Yield a = Handler.return' a
  member _.Zero() = Handler.return' ()
  member inline _.Delay a = a
  member inline _.Run a = a ()

  member _.Combine(m1, m2) =
    m1 () |> Handler.bind (fun _ -> m2)

  member this.While(guard, body) =
    if not (guard ()) then
      this.Zero()
    else
      Handler.bind (fun () -> this.While(guard, body)) body

  member _.TryFinally(body, compensation) =
    try
      body ()
    finally
      compensation ()

  member this.Using(disposable: #System.IDisposable, body) =
    this.TryFinally(
      (fun () -> body disposable),
      (fun () ->
        match disposable with
        | null -> ()
        | disp -> disp.Dispose())
    )

  member this.For(sequence: seq<_>, body) =
    this.Using(
      sequence.GetEnumerator(),
      fun enum ->
        this.While(enum.MoveNext, body enum.Current)
    )

  member inline _.Bind
    (x: Handler<_>, [<InlineIfLambda>] f)
    =
    Handler.bind f x

  [<CustomOperation("update_ctx")>]
  member this.UpdateCtx(soFar, f) =
    this.Combine(soFar, Handler.updateCtx f)

  [<CustomOperation("including")>]
  member this.Including(soFar, included: Handler<unit>) =
    this.Combine(soFar, included)

let handler = HandlerBuilder()

(*
            TEMPLATES
            Helpers for passing around fragments of HTML that may need
            http context info
 *)
type ContextualTemplate =
  Handler<string -> Markup.XmlNode list -> Markup.XmlNode>

let HxFragment targetId template =
  handler {
    let! headers = Handler.fromCtx Request.getHeaders
    let hxRequest = headers.GetBoolean("hx-request", false)

    return
      if hxRequest then
        Response.ofFragment targetId template
      else
        Response.ofHtml template
  }

module HxMorph =
  let morphOuterHtml =
    Markup.Attr.create "hx-swap" "morph:outerHTML"

  let morphInnerHtml =
    Markup.Attr.create "hx-swap" "morph:innerHTML"

(*
            SERVICES
            Helpers for consistent patterns in setting up our "modules" as
            aspnetcore services
*)
type AddService =
  IConfiguration -> IServiceCollection -> IServiceCollection

type ServiceEndpoints = string -> HttpEndpoint seq
