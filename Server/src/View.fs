module VisualInk.Server.View

open Prelude
open Falco
open Falco.Markup

type ViewContext =
  { skeletalTemplate: string -> XmlNode list -> XmlNode
    contextualTemplate: ContextualTemplate
    navbar: Handler<XmlNode>
    notFound: HttpHandler
  }
