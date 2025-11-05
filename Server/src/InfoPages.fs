module VisualInk.Server.InfoPages

open Prelude
open View
open Falco
open Falco.Routing
open Falco.Markup
open Falco.Htmx
open Markdig
open Microsoft.AspNetCore.Hosting
open System.IO

module B = Bulma

(*
       This module contains all those things that your website needs like a privacy
       notice and an 'About Us' section.

       The versions here are for the publicly deployed version of VisualInk hosted
       by myself as the project author, so you'll want to adapt these if you're
       going to self host the project.

       *)

let private privacyNotice viewContext =
  handler {
    let! template = viewContext.contextualTemplate

    let! hostEnv = Handler.plug<IWebHostEnvironment, _> ()

    let! markdown =
      File.ReadAllTextAsync(
        Path.Join(
          hostEnv.ContentRootPath,
          "content/infoPages/privacy.md"
        )
      )
      |> Handler.returnTask
      |> Handler.map Markdown.ToHtml

    let content =
      [ B.content [ Hx.boostOff ] [ _text markdown ] ]

    return
      Response.ofHtml (template "Privacy Notice" content)
  }
  |> Handler.flatten
  |> get "/info/privacy"

let private about viewContext =
  handler {
    let! template = viewContext.contextualTemplate
    let! hostEnv = Handler.plug<IWebHostEnvironment, _> ()

    let! markdown =
      File.ReadAllTextAsync(
        Path.Join(
          hostEnv.ContentRootPath,
          "content/infoPages/about.md"
        )
      )
      |> Handler.returnTask
      |> Handler.map Markdown.ToHtml

    let content =
      [ B.content [ Hx.boostOff ] [ _text markdown ] ]

    return Response.ofHtml (template "About us" content)
  }
  |> Handler.flatten
  |> get "/info/about"


let footer: Handler<XmlNode, HttpHandler> =
  handler {
    let! hostEnv = Handler.plug<IWebHostEnvironment, _> ()

    let! markdown =
      File.ReadAllTextAsync(
        Path.Join(
          hostEnv.ContentRootPath,
          "content/infoPages/footer_text.md"
        )
      )
      |> Handler.returnTask
      |> Handler.map Markdown.ToHtml

    return
      B.content
        [ _class_ "has-text-centered" ]
        [ _text markdown ]
      |> B.encloseAttr
        B.footer
        [ _id_ "footer"; _style_ "bottom: 0px;" ]
  }

let nav =
  B.navbarItemA
    [ _href_ "/info/about" ]
    [ _text "About Us" ]

module Service =
  let endpoints viewContext =
    [ privacyNotice viewContext; about viewContext ]

  let addService: AddService = fun _ -> id
