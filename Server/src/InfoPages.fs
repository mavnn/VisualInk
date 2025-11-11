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
let private mdToHtml path =
  handler {
    let! hostEnv = Handler.plug<IWebHostEnvironment, _> ()

    let! markdown =
      File.ReadAllTextAsync(
        Path.Join(hostEnv.ContentRootPath, path)
      )
      |> Handler.returnTask
      |> Handler.map Markdown.ToHtml

    return markdown
  }

let private mdPage title path viewContext =
  handler {
    let! template = viewContext.contextualTemplate

    let! markdown =
      mdToHtml path |> Handler.map Markdown.ToHtml

    let content =
      [ B.content [ Hx.boostOff ] [ _text markdown ] ]

    return Response.ofHtml (template title content)
  }

let private privacyNotice viewContext =
  mdPage
    "Privacy Notice"
    "content/infoPages/privacy.md"
    viewContext
  |> Handler.flatten
  |> get "/info/privacy"

let private about viewContext =
  mdPage "About us" "content/infoPages/about.md" viewContext
  |> Handler.flatten
  |> get "/info/about"

let private inkGuide viewContext =
  mdPage
    "The Ink cheat sheet"
    "content/infoPages/a_guide_to_ink.md"
    viewContext
  |> Handler.flatten
  |> get "/info/ink_guide"

let footer: Handler<XmlNode, HttpHandler> =
  handler {
    let! markdown =
      mdToHtml "content/infoPages/footer_text.md"

    return
      B.content
        [ _class_ "has-text-centered" ]
        [ _text markdown ]
      |> B.encloseAttr
        B.footer
        [ _id_ "footer"; _style_ "bottom: 0px;" ]
  }

let nav =
  B.navbarDropdown
    []
    {| link = "Help and Info"
       dropdown =
        [ B.navbarItemA
            [ _href_ "/info/ink_guide" ]
            [ _text "Ink cheat sheet" ]
          B.navbarItemA
            [ _href_ "/info/about" ]
            [ _text "About Us" ] ]

    |}

module Service =
  let endpoints viewContext =
    [ privacyNotice viewContext
      about viewContext
      inkGuide viewContext ]

  let addService: AddService = fun _ -> id
