module VisualInk.Server.Bulma

open Falco.Markup
open Falco.Security

let enclose wrapper item = wrapper [] [ item ]

let encloseAttr wrapper attrs item = wrapper attrs [ item ]

let container attr content =
  _div (Attr.merge [ _class_ "container" ] attr) content

module Mods =
  let isFullwidth = "is-fullwidth"
  let isSmall = "is-small"
  let isDanger = "is-danger"
  let isPrimary = "is-primary"
  let isLink = "is-link"
  let isInfo = "is-info"
  let help = "help"

let content attr content =
  _div (Attr.merge [ _class_ "content" ] attr) content

let form token attr content =
  _form attr (Xsrf.antiforgeryInput token :: content)

module FieldMods =
  let isGrouped = "is-grouped"
  let isGroupedCentered = "is-grouped-centered"

type FieldInfo =
  { label: string option
    input: XmlNode list
    field: XmlAttribute list
    control: XmlAttribute list }

  static member empty =
    { label = None
      input = []
      field = []
      control = [] }

module ControlMods =
  let hasIconsLeft = "has-icons-left"

let field setInfo =
  let info = setInfo FieldInfo.empty

  _div
    (Attr.merge [ _class_ "field" ] info.field)
    [ match info.label with
      | Some l ->
        yield _label [ _class_ "label" ] [ _text l ]
      | None -> ()
      yield
        _div
          (Attr.merge [ _class_ "control" ] info.control)
          info.input ]

type FieldHorizontalInput =
  { label: string option
    body: XmlNode list }

let fieldHorizontal attr input =
  _div
    (Attr.merge [ _class_ "field is-horizontal" ] attr)
    [ _div
        [ _class_ "field-label is-normal" ]
        [ _label
            [ _class_ "label" ]
            [ _text (Option.defaultValue "" input.label) ] ]
      _div [ _class_ "field-body" ] input.body ]

let input attr =
  _input (Attr.merge [ _class_ "input" ] attr)

let select
  (attrs:
    {| selectAttrs: XmlAttribute list
       wrapperAttrs: XmlAttribute list |})
  content
  =
  _div
    (Attr.merge [ _class_ "select" ] attrs.wrapperAttrs)
    [ _select attrs.selectAttrs content ]

let button attr content =
  _button (Attr.merge [ _class_ "button" ] attr) content

let buttons attr buttons =
  buttons
  |> List.map (fun (attr, content) -> button attr content)
  |> _div (Attr.merge [ _class_ "buttons" ] attr)

let image figure img =
  _figure
    (Attr.merge [ _class_ "image" ] figure)
    [ _img img ]

let block attr content =
  _div (Attr.merge [ _class_ "block" ] attr) content

let grid attr cells =
  _div (Attr.merge [ _class_ "grid" ] attr) cells

let fixedGrid attr cells =
  grid
    []
    [ _div
        (Attr.merge [ _class_ "fixed-grid" ] attr)
        [ grid [] cells ] ]

let cell attr content =
  _div (Attr.merge [ _class_ "cell" ] attr) content

let title attr text =
  _h1 (Attr.merge [ _class_ "title" ] attr) [ _text text ]

let subtitle attr text =
  _h2
    (Attr.merge [ _class_ "subtitle" ] attr)
    [ _text text ]

let section attr content =
  _section (Attr.merge [ _class_ "section" ] attr) content

type NavbarInput =
  { brand: XmlNode list
    menu: XmlNode list }

let navbar attr input =
  let burger =
    _a
      [ _class_ "navbar-burger"
        _role_ "button"
        _id_ "navbar-burger"
        _onclick_
          "(() => [document.getElementById('navbar-burger'), document.getElementById('navbar-menu')].map((e) => e.classList.toggle('is-active')))();return false"
        Attr.create "aria-label" "menu"
        Attr.create "aria-expanded" "false" ]
      [ _span [ Attr.create "aria-hidden" "true" ] []
        _span [ Attr.create "aria-hidden" "true" ] []
        _span [ Attr.create "aria-hidden" "true" ] []
        _span [ Attr.create "aria-hidden" "true" ] [] ]

  _nav
    (Attr.merge [ _class_ "navbar" ] attr)
    [ _div
        [ _class_ "container" ]
        [ _div
            [ _class_ "navbar-brand" ]
            (List.append input.brand [ burger ])
          _div
            [ _class_ "navbar-menu"; _id_ "navbar-menu" ]
            input.menu ] ]

let navbarStart attr content =
  _div (Attr.merge [ _class_ "navbar-start" ] attr) content

let navbarEnd attr content =
  _div (Attr.merge [ _class_ "navbar-end" ] attr) content

let navbarItemSpan attr content =
  _span (Attr.merge [ _class_ "navbar-item" ] attr) content

let navbarItemA attr content =
  _a (Attr.merge [ _class_ "navbar-item" ] attr) content

let navbarDivider = _hr [ _class_ "navbar-divider" ]

let navbarDropdown
  attr
  (content:
    {| link: string
       dropdown: XmlNode list |})
  =
  _div
    (Attr.merge
      [ _class_ "navbar-item has-dropdown is-hoverable" ]
      attr)
    [ navbarItemA [] [ _text content.link ]
      _div [ _class_ "navbar-dropdown" ] content.dropdown ]

module IconMods =
  let isLeft = "is-left"
  let isRight = "is-right"

let icon name attr =
  _span
    (Attr.merge [ _class_ "icon" ] attr)
    [ _i [ _class_ $"mdi mdi-{name}" ] [] ]

let iconText attr content =
  _span (Attr.merge [ _class_ "icon-text" ] attr) content

type CardInput =
  { image: XmlNode list
    content: XmlNode list }

let card attr contents =
  _div
    (Attr.merge [ _class_ "card" ] attr)
    [ _div [ _class_ "card-image" ] contents.image
      _div [ _class_ "card-content" ] contents.content ]

let delete attr =
  _div (Attr.merge [ _class_ "delete" ] attr) []

let footer attr content =
  _footer (Attr.merge [ _class_ "footer" ] attr) content

let columns attr content =
  _div (Attr.merge [ _class_ "columns" ] attr) content

let column attr content =
  _div (Attr.merge [ _class_ "column" ] attr) content

let notification attr content =
  _div (Attr.merge [ _class_ "notification" ] attr) content

let box attr content =
  _div (Attr.merge [ _class_ "box" ] attr) content

type MenuLinkItem =
  { attr: XmlAttribute list
    text: string }

type MenuSublistItem =
  { sublabel: MenuLinkItem
    items: MenuLinkItem list }

type MenuItem =
  | MenuLink of MenuLinkItem
  | MenuSublist of MenuSublistItem

  static member Node(item: MenuItem) =
    match item with
    | MenuLink linkItem ->
      _text linkItem.text
      |> encloseAttr _a linkItem.attr
      |> enclose _li
    | MenuSublist sublist ->
      _li
        []
        [ MenuItem.Node(
            MenuLink
              { attr =
                  Attr.merge
                    [ _class_ "is-active" ]
                    sublist.sublabel.attr
                text = sublist.sublabel.text }
          )
          sublist.items
          |> List.map (MenuLink >> MenuItem.Node)
          |> _ul [] ]


type MenuInput = { label: string; items: MenuItem list }

let menu attr items =
  _aside
    (Attr.merge [ _class_ "menu" ] attr)
    (items
     |> List.collect (fun input ->
       let itemNodes =
         input.items |> List.map MenuItem.Node

       [ _p [ _class_ "menu-label" ] [ _text input.label ]
         if input.items.Length <> 0 then
           yield! [ _ul [ _class_ "menu-list" ] itemNodes ] ]))

type DetailMessageInput =
  { content: XmlNode list
    summary: XmlNode list
    summaryAttr: XmlAttribute list
    messageAttr: XmlAttribute list }

let detailMessage setOpts =
  let opts =
    setOpts
      { content = []
        summary = []
        summaryAttr = []
        messageAttr = [] }

  // _details
  //   (Attr.merge [ _class_ "message" ] opts.messageAttr)
  //   [ _summary
  //       (Attr.merge
  //         [ _class_ "message-header" ]
  //         opts.summaryAttr)
  //       [ iconText
  //           []
  //           (icon "arrow-down-bold" [] :: opts.summary) ]
  //     _div [ _class_ "message-body" ] opts.content ]
  _details
    (Attr.merge [ _class_ "block" ] opts.messageAttr)
    [ _summary
        (Attr.merge
          [  ]
          opts.summaryAttr)
        [ iconText
            []
            // (icon "arrow-down-bold" [] :: opts.summary) ]
             opts.summary ]
      _div [  ] opts.content ]
