module VisualInk.Server.Prelude

open System.Threading.Tasks
open Falco
open Falco.Htmx
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

type Handler<'a, 'error> =
  HttpContext -> Task<HttpContext * Result<'a, 'error>>

module Handler =
  let inline return' x : Handler<_, 'error> =
    fun ctx -> Task.FromResult(ctx, Ok x)

  let returnTask x : Handler<_, 'error> =
    fun ctx ->
      task {
        let! v = x
        return! return' v ctx
      }

  let returnTask' (x: Task) : Handler<_, 'error> =
    fun ctx ->
      task {
        let! v = x
        return ctx, Ok v
      }

  let inline bind
    ([<InlineIfLambda>] f: 'a -> Handler<'b, 'error>)
    ([<InlineIfLambda>] x: Handler<'a, 'error>)
    : Handler<'b, 'error> =
    fun ctx ->
      task {
        let! newCtx, soFar = x ctx

        match soFar with
        | Ok v -> return! f v newCtx
        | Error err -> return newCtx, Error err
      }

  let map f x = bind (fun v -> return' (f v)) x

  let mapError f x =
    fun (ctx: HttpContext) ->
      task {
        let! result = x ctx

        match result with
        | Error e -> return Error(f e)
        | Ok v -> return Ok v
      }

  let collect f xs =
    Seq.fold
      (fun acc next ->
        acc
        |> bind (fun results ->
          f next |> map (fun result -> result :: results)))
      (return' [])
      xs
    |> map List.rev

  let getCtx (ctx: HttpContext) =
    Task.FromResult(ctx, Ok ctx)

  let updateCtx f : Handler<unit, 'error> =
    fun ctx -> Task.FromResult(f ctx, Ok())

  let fromCtx f : Handler<_, 'error> = getCtx |> map f

  let fromCtxTask f : Handler<_, 'error> =
    getCtx |> bind (f >> returnTask)

  let plug<'Interface, 'error>
    ()
    : Handler<'Interface, 'error> =
    fromCtx (fun ctx -> ctx.Plug<'Interface>())

  let failure errHandler : Handler<_, 'error> =
    fun ctx -> Task.FromResult(ctx, Error errHandler)

  let getCsrfToken
    ()
    : Handler<
        Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet,
        _
       >
    =
    fromCtx Xsrf.getToken

  let formDataOrFail onFailure mapForm : Handler<_, _> =
    fromCtxTask Request.getFormSecure
    |> bind (fun maybeFormCollection ->
      match maybeFormCollection with
      | Some formCollection ->
        match mapForm formCollection with
        | Some v -> return' v
        | None -> failure onFailure
      | None -> failure onFailure)

  let queryData mapQuery =
    fromCtx Request.getQuery
    |> map mapQuery

  let ofOption onFailure value =
    value
    |> bind (fun v ->
      match v with
      | Some v -> return' v
      | None -> failure onFailure)

  let bindOption f handlerOption =
    handlerOption
    |> bind (fun opt ->
      match opt with
      | Some v -> f v
      | None -> return' None)

  let mapOption f handlerOption =
    handlerOption |> map (Option.map f)

  let flatten
    (handler: Handler<HttpHandler, HttpHandler>)
    : HttpHandler =
    fun ctx ->
      task {
        match! handler ctx with
        | ctx', Ok success -> return! success ctx'
        | ctx', Error failure -> return! failure ctx'
      }

  let applyIn (f: Handler<'a -> 'b, _>) (v: 'a) =
    map (fun f' -> f' v) f

  let tap f = bind (fun v -> map (fun () -> v) (f v))

  let getHrefForFilePath filePath =
    plug<IWebHostEnvironment, _> ()
    |> map (fun h ->
      "/"
      + System.IO.Path.GetRelativePath(
        h.WebRootPath,
        filePath
      ))

  let createAbsoluteLink path =
    fromCtx (fun ctx ->
      let host = ctx.Request.Host.Value

      let scheme =
        if host.StartsWith "localhost" then
          "http://"
        else
          "https://"

      $"{scheme}{host}{path}")


type HandlerBuilder() =
  member _.Return a = Handler.return' a
  member _.ReturnFrom(a: Handler<_, _>) = a
  member _.Yield a = Handler.return' a
  member _.Zero() = Handler.return' ()
  member inline _.Delay a = a
  member inline _.Run m = m ()

  member _.Combine(m1, m2) =
    m1 |> Handler.bind (fun () -> m2 ())

  member this.While(guard, body) =
    if not (guard ()) then
      this.Zero()
    else
      Handler.bind
        (fun () -> this.While(guard, body))
        (body ())

  member _.TryFinally(body, compensation) =
    fun ctx ->
      task {
        try
          return! body () ctx
        finally
          compensation ()
      }

  member _.TryWith
    (body: unit -> Handler<'a, _>, compensation)
    : Handler<'a, _> =
    fun (ctx: HttpContext) ->
      task {
        try
          let! result = body () ctx
          return result
        with e ->
          return! compensation e ctx
      }

  member _.Using(disposable: #System.IDisposable, body) =
    fun ctx ->
      task {
        try
          return! body disposable ctx
        finally
          disposable.Dispose()
      }

  member this.For
    (sequence: seq<'a>, body: 'a -> Handler<_, _>)
    : Handler<_, _> =
    sequence
    |> Seq.map (fun a -> body a)
    |> Seq.reduce (fun m1 m2 ->
      this.Combine(m1, fun () -> m2))

  member inline _.Bind
    (x: Handler<_, _>, [<InlineIfLambda>] f)
    =
    Handler.bind f x

  [<CustomOperation("update_ctx")>]
  member this.UpdateCtx(soFar, f) =
    this.Combine(soFar, fun () -> Handler.updateCtx f)

  [<CustomOperation("including")>]
  member this.Including(soFar, included: Handler<unit, _>) =
    this.Combine(soFar, fun () -> included)

let handler = HandlerBuilder()

(*
            TEMPLATES
            Helpers for passing around fragments of HTML that may need
            http context info
 *)
type ContextualTemplate =
  Handler<
    string -> Markup.XmlNode list -> Markup.XmlNode,
    HttpHandler
   >

let checkHxRequest () =
  handler {
    let! headers = Handler.fromCtx Request.getHeaders
    return headers.GetBoolean("hx-request", false)
  }

let hxRedirect url =
  handler {
    let! hxRequest = checkHxRequest()
    if hxRequest then
      return Response.withHxRedirect url >> Response.withStatusCode 201 >> Response.ofEmpty
    else
      return Response.redirectTemporarily url
  }

let HxFragment targetId template =
  handler {
    let! hxRequest = checkHxRequest()

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

let serializeJson =
  System.Text.Json.JsonSerializer.Serialize

module Slug =
  open Sqids
  let private alphabet =
          "hC5opuPZH3B2jML1t76wXmNDQYJUAkRGKT4vFfq9Sx8EbcngdiyeVrWzas"

  let sqids =
    SqidsEncoder<uint64>(
      SqidsOptions(
        Alphabet = alphabet
      )
    )

  let toGuid (sqid: string) =
    sqids.Decode sqid
    |> Seq.toArray
    |> Array.collect System.BitConverter.GetBytes
    |> System.Guid

  let fromGuid (guid: System.Guid) =
    let bytes = guid.ToByteArray()
    let front = System.BitConverter.ToUInt64 bytes.[0..7]
    let back = System.BitConverter.ToUInt64 bytes.[8..15]
    sqids.Encode [| front; back |]

module Content =
  open System.IO

  let getContentText path =
    handler {
      let! hostEnv = Handler.plug<IWebHostEnvironment, _> ()

      return!
        File.ReadAllTextAsync(
          Path.Join(
            hostEnv.ContentRootPath,
            "content",
            path
          )
        )
        |> Handler.returnTask
    }

module List =
  /// The intersperse function takes an element and a list and
  /// 'intersperses' that element between the elements of the list.
  let intersperse sep ls =
    List.foldBack
      (fun x ->
        function
        | [] -> [ x ]
        | xs -> x :: sep :: xs)
      ls
      []

module Option =
  let ofTry<'v> (tried: bool * 'v) =
    match tried with
    | true, v -> Some v
    | false, _ -> None
