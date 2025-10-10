module VisualInk.Server.User

open Falco
open Falco.Routing
open Falco.Markup
open Falco.Htmx
open Marten
open Marten.Linq.MatchesSql
open System.Linq
open Marten.Events.Aggregation
open System.Security.Claims
open Microsoft.AspNetCore.Identity
open Prelude
open View
open JasperFx.Events.Projections
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Authentication
open System.Text.Json.Serialization
open Falco.Security
open Microsoft.Extensions.Logging

module B = Bulma

type User = { id: System.Guid; username: string }

[<JsonFSharpConverter>]
type UserState =
  | Active
  | Disabled

[<JsonFSharpConverter>]
[<CLIMutable>]
type GoogleInfo = { googleId: string }

[<JsonFSharpConverter>]
type ExternalProviderInfo = GoogleInfo of GoogleInfo

[<JsonFSharpConverter>]
[<CLIMutable>]
type UserRecord =
  { Id: System.Guid
    Username: string
    PasswordHash: string option
    State: UserState
    ExternalInfo: ExternalProviderInfo option }

[<JsonFSharpConverter>]
type UserEvent =
  | Created of UserRecord
  | PasswordChanged of passwordHash: string
  | Disabled

type UserRecordProjection() =
  inherit SingleStreamProjection<UserRecord, System.Guid>()

  member _.Create
    (userEvent: JasperFx.Events.IEvent<UserEvent>)
    =
    match userEvent.Data with
    | Created user -> user
    | _ ->
      // We should always receive a created event
      // first so this shouldn't ever happen...
      // ...but it might, and we don't want to throw
      // in projections.
      { Id = userEvent.StreamId
        Username = ""
        PasswordHash = None
        State = UserState.Disabled
        ExternalInfo = None }


  member _.Apply(userEvent, userRecord: UserRecord) =
    task {

      match userEvent with
      | Created _ ->
        // Should never occur after the first event in the stream
        // so we ignore duplicates
        return userRecord
      | PasswordChanged passwordHash ->
        match userRecord with
        | { State = UserState.Disabled } ->
          // Don't update password of disabled users
          return userRecord
        | user ->
          return
            { user with
                PasswordHash = Some passwordHash }
      | Disabled ->
        match userRecord with
        | { State = UserState.Disabled } ->
          return userRecord
        | { State = Active } ->
          return
            { userRecord with
                State = UserState.Disabled }
    }

type private LoginFormData =
  { username: string
    password: string
    invite: string option }

type private GoogleRedirectData = { credential: string }

type private LoginData =
  | LoginFormData of LoginFormData
  | GoogleRedirectData of GoogleRedirectData

let private findUserRecordFrom (googleId: string) =
  handler {
    let! documentStore =
      Handler.fromCtx (fun ctx ->
        ctx.Plug<IDocumentStore>())

    let session = documentStore.QuerySession()

    let! user =
      session
        .Query<UserRecord>()
        .Where(fun x ->
          x.MatchesSql(
            "data -> 'ExternalInfo' ->> 'googleId' = ?",
            googleId
          ))
        .SingleOrDefaultAsync()
      |> Handler.returnTask


    return user
  }
  |> Handler.map Option.ofObj


let private findUserRecord (username: string) =
  handler {
    let! documentStore =
      Handler.fromCtx (fun ctx ->
        ctx.Plug<IDocumentStore>())

    let session = documentStore.QuerySession()

    return!
      Handler.returnTask (
        session
          .Query<UserRecord>()
          .SingleOrDefaultAsync(fun ur ->
            ur.Username = username)
      )
  }
  |> Handler.map Option.ofObj


let private updateUser
  (id: System.Guid, events: seq<UserEvent>)
  =
  handler {
    let! documentStore =
      Handler.fromCtx (fun ctx ->
        ctx.Plug<IDocumentStore>())

    let session = documentStore.LightweightSession()

    let eventObjs: obj[] =
      Array.ofSeq events |> Array.map box

    session.Events.Append(id, eventObjs) |> ignore

    do!
      Handler.returnTask (
        task { do! session.SaveChangesAsync() }
      )

    return!
      Handler.returnTask (session.LoadAsync<UserRecord> id)
  }

let private createUser (evt: UserEvent) =
  handler {
    let! logger = Handler.plug<ILogger<UserRecord>>()
    let source =
      match evt with
      | Created { ExternalInfo = Some (GoogleInfo _) } -> "Google"
      | _ -> "invite code"
    logger.LogInformation ("New user created via {source}", source)
    let! documentStore =
      Handler.fromCtx (fun ctx ->
        ctx.Plug<IDocumentStore>())

    let session = documentStore.LightweightSession()

    let result = session.Events.StartStream<UserRecord> evt
    do!
      Handler.returnTask (
        task { do! session.SaveChangesAsync() }
      )

    logger.LogInformation ("User record for {id} stored", result.Id)

    return!
      Handler.returnTask (session.LoadAsync<UserRecord> result.Id)
  }

let getSessionUserFromCtx (ctx: HttpContext) =
  match ctx.User with
  | null -> None
  | principal ->
    match
      System.Guid.TryParse(
        principal.FindFirstValue "userId"
      ),
      principal.FindFirstValue "name"
    with
    | (false, _), _
    | _, null -> None
    | (true, id), username ->
      Some { id = id; username = username }

let getSessionUser: Handler<User option> =
  Handler.fromCtx getSessionUserFromCtx

let ensureSessionUser: Handler<User> =
  getSessionUser
  |> Handler.bind (fun maybeUser ->
    match maybeUser with
    | Some user -> Handler.return' user
    | None ->
      Handler.failure (
        Response.redirectTemporarily "/user/login"
      ))

type UserFormInput =
  { location: string
    usernameValue: string option
    usernameProblem: string option
    passwordValue: string option
    passwordProblem: string option
    isSignup: bool }

let private userForm viewContext title input =
  handler {
    let clientId =
      System.Environment.GetEnvironmentVariable
        "GOOGLE_OAUTH_CLIENT_ID"
      |> Option.ofObj
      |> Option.bind (fun cid ->
        if cid.IsEmpty() then None else Some cid)

    let userInput =
      B.field (fun info ->
        { info with
            label = Some "Username"
            control =
              [ _class_ B.ControlMods.hasIconsLeft ]
            input =
              [ B.input
                  [ _type_ "text"
                    _placeholder_ "Your username"
                    _name_ "username"
                    match input.usernameValue with
                    | Some u -> yield! [ _value_ u ]
                    | None -> yield! [] ]
                B.icon
                  "account"
                  [ _class_
                      $"{B.Mods.isSmall} {B.IconMods.isLeft}" ]
                match input.usernameProblem with
                | Some p ->
                  yield!
                    [ _p
                        [ _class_
                            $"{B.Mods.help} {B.Mods.isDanger}" ]
                        [ _text p ] ]
                | None -> yield! [] ] })

    let passwordInput =
      B.field (fun info ->
        { info with
            label = Some "Password"
            control =
              [ _class_ B.ControlMods.hasIconsLeft ]
            input =
              [ B.input
                  [ _type_ "password"
                    _placeholder_ "Your password"
                    _name_ "password"
                    match input.passwordValue with
                    | Some u -> yield! [ _value_ u ]
                    | None -> yield! [] ]
                B.icon
                  "lock"
                  [ _class_
                      $"{B.Mods.isSmall} {B.IconMods.isLeft}" ]
                match input.passwordProblem with
                | Some p ->
                  yield!
                    [ _p
                        [ _class_
                            $"{B.Mods.help} {B.Mods.isDanger}" ]
                        [ _text p ] ]
                | None -> yield! [] ] })

    let inviteCode =
      B.field (fun info ->
        { info with
            label = Some "Invite code"
            input =
              [ B.input
                  [ _type_ "text"
                    _placeholder_ "Your invite code"
                    _name_ "invite" ] ] })

    let submitButton =
      B.field (fun info ->
        { info with
            input =
              [ B.button
                  [ _class_ B.Mods.isPrimary
                    Hx.post input.location
                    Hx.targetCss "#user-form"
                    Hx.indicator "#user-form button"
                    HxMorph.morphOuterHtml
                    _type_ "submit"
                    _name_ "submit" ]
                  [ _text "Submit" ] ] })

    let! token = Handler.getCsrfToken
    let! template = viewContext.contextualTemplate
    let! redirectUri = Handler.createAbsoluteLink "/user/login"

    return
      template
        title
        [ B.form
            token
            [ _id_ "user-form" ]
            [ userInput
              passwordInput
              if input.isSignup then
                yield! [ inviteCode ]
              submitButton ]
          |> B.enclose B.block

          match clientId with
          | None -> ()
          | Some cid ->
            yield!
              [ B.block
                  []
                  [ _div
                      [ _id_ "g_id_onload"
                        Attr.create "data-client_id" cid
                        Attr.create
                          "data-login_uri"
                          redirectUri
                        Attr.create
                          "data-ux_mode"
                          "redirect" ]
                      []
                    _div [ _class_ "g_id_signin" ] [] ] ]
          _script
            [ _src_ "https://accounts.google.com/gsi/client"
              _async_ ]
            [] ]
  }

let private makePrincipal userRecord =
  let claims =
    [ new Claim("name", userRecord.Username)
      new Claim("userId", userRecord.Id.ToString()) ]

  let identity = new ClaimsIdentity(claims, "Cookies")

  new ClaimsPrincipal(identity)

let private passwordHasher = PasswordHasher()

let private signIn authScheme principal url =
  handler {
    do!
      Handler.fromCtxTask (fun ctx ->
        task {
          do! Response.signIn authScheme principal ctx
        })

    let! hxHeaders = Handler.fromCtx Request.getHtmxHeaders
    match hxHeaders.HxRequest with
    | Some "true" ->
        do!
          Handler.fromCtx (
            Response.withHeaders [ "HX-Location", url ]
            >> ignore
          )
        return Response.ofEmpty
    | Some _
    | None ->
        return Response.redirectTemporarily "/"
  }

let private getLoginFormData
  loginData
  location
  viewContext
  =
  handler {
    let noUsername = "You must provide a username"
    let noPassword = "You must provide a password"

    match
      loginData.username.Length, loginData.password.Length
    with
    | 0, 0 ->
      let! form =
        userForm
          viewContext
          "Log in"
          { location = location
            usernameValue = None
            usernameProblem = Some noUsername
            passwordValue = None
            passwordProblem = Some noPassword
            isSignup = false }

      return!
        Handler.failure (
          HxFragment "user-form" form |> Handler.flatten
        )
    | 0, _ ->
      let! form =
        userForm
          viewContext
          "Log in"
          { location = location
            usernameValue = None
            usernameProblem = Some noUsername
            passwordValue = Some loginData.password
            passwordProblem = None
            isSignup = false }

      return!
        Handler.failure (
          HxFragment "user-form" form |> Handler.flatten
        )
    | _, 0 ->
      let! form =
        userForm
          viewContext
          "Log in"
          { location = location
            usernameValue = Some loginData.username
            usernameProblem = None
            passwordValue = None
            passwordProblem = Some noPassword
            isSignup = false }

      return!
        Handler.failure (
          HxFragment "user-form" form |> Handler.flatten
        )
    | _, _ -> return LoginFormData loginData
  }

let private getLoginData location viewContext =
  handler {
    // If the fields haven't been sent at all,
    // send a 400 error; this is a bad request
    // not a user typing the wrong thing.
    // Note that we check the xsrf token manually
    // here as it won't exist on the google redirect
    // requests.
    let! loginData =
      Handler.fromCtxTask Request.getForm
      |> Handler.bind (fun f ->
        handler {
          match f.TryGetString "credential" with
          | Some creds ->
            let! cookies =
              Handler.fromCtx Request.getCookies

            let googleXsrfCookie =
              cookies.TryGetStringNonEmpty "g_csrf_token"

            let googleXsrfBody =
              f.TryGetStringNonEmpty "g_csrf_token"

            let xsrfValid = 
              googleXsrfCookie.IsSome
              && googleXsrfCookie = googleXsrfBody
            // let claimedClientId = f.TryGetStringNonEmpty "clientId"


            // let clientIdValid =
            //    claimedClientId = Some (System.Environment.GetEnvironmentVariable "GOOGLE_OAUTH_CLIENT_ID")

            // if xsrfValid && clientIdValid then
            if xsrfValid then
              return
                Some(
                  GoogleRedirectData { credential = creds }
                )
            else
              return None
          | None ->
            let! isValid =
              Handler.fromCtxTask Xsrf.validateToken

            if isValid then
              return
                Option.map2
                  (fun username password ->
                    LoginFormData
                      { username = username
                        password = password
                        invite = f.TryGetString "invite" })
                  (f.TryGetString "username")
                  (f.TryGetString "password")
            else
              return None
        })
      |> Handler.ofOption (
        Response.withStatusCode 400 >> Response.ofEmpty
      )

    match loginData with
    | LoginFormData formData ->
      return! getLoginFormData formData location viewContext
    | GoogleRedirectData _ -> return loginData
  }

let private authenticationFailed
  viewContext
  formData
  location
  =
  let failedAuth =
    "Matching username and password not found"

  userForm
    viewContext
    "Log in"
    { location = location
      usernameValue = Some formData.username
      usernameProblem = Some failedAuth
      passwordValue = Some formData.password
      passwordProblem = Some failedAuth
      isSignup = false }
  |> Handler.bind (HxFragment "user-form")

let private loginGetEndpoint viewContext =
  handler {
    let location = "/user/login"

    let! form =
      userForm
        viewContext
        "Log in"
        { location = location
          usernameValue = None
          usernameProblem = None
          passwordValue = None
          passwordProblem = None
          isSignup = false }

    return Response.ofHtml form
  }
  |> Handler.flatten
  |> get "/user/login"

let private loginViaForm loginData location viewContext =
  handler {
    let! userRecord =
      findUserRecord loginData.username
      |> Handler.ofOption (
        authenticationFailed viewContext loginData location
        |> Handler.flatten
      )

    let! verificationResult =
      match userRecord.PasswordHash with
      | Some hash ->
        passwordHasher.VerifyHashedPassword(
          userRecord,
          hash,
          loginData.password
        )
        |> Handler.return'
      | None ->
        Handler.failure (
          Response.withStatusCode 403 >> Response.ofEmpty
        )

    match verificationResult with
    | PasswordVerificationResult.Failed ->
      return!
        authenticationFailed viewContext loginData location
        |> Handler.flatten
        |> Handler.failure
    | PasswordVerificationResult.Success ->
      return! signIn "Cookies" (makePrincipal userRecord) "/"
    | PasswordVerificationResult.SuccessRehashNeeded ->
      let! _ =
        updateUser (
          userRecord.Id,
          [ PasswordChanged(
              passwordHasher.HashPassword(
                userRecord,
                loginData.password
              )
            ) ]
        )

      return! signIn "Cookies" (makePrincipal userRecord) "/"
    | _ ->
      return
        failwithf
          "Unknown password verification result type %O"
          verificationResult
  }

let private loginPostEndpoint viewContext =
  handler {
    let location = "/user/login"
    let! loginData = getLoginData location viewContext

    match loginData with
    | LoginFormData formData ->
      return! loginViaForm formData location viewContext
    | GoogleRedirectData redirectData ->
      let! token =
        Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync
          redirectData.credential
        |> Handler.returnTask

      let googleSubjectId = token.Subject

      let! existingUser = findUserRecordFrom googleSubjectId

      match existingUser with
      | Some user ->
        return! signIn "Cookies" (makePrincipal user) "/"
      | None ->
        let newId = System.Guid.NewGuid()

        let! newUser =
          createUser (
            Created
              { Id = newId
                Username = token.Email
                PasswordHash = None
                State = Active
                ExternalInfo =
                  Some(
                    GoogleInfo
                      { googleId = googleSubjectId }
                  ) }
          )

        return! signIn "Cookies" (makePrincipal newUser) "/"
  }
  |> Handler.flatten
  |> post "/user/login"

let private logoutEndpoint =
  handler {
    return Response.signOutAndRedirect "Cookies" "/"
  }
  |> Handler.flatten
  |> get "/user/logout"

let private signupGetEndpoint viewContext =
  userForm
    viewContext
    "Sign up"
    { location = "/user/signup"
      usernameValue = None
      usernameProblem = None
      passwordValue = None
      passwordProblem = None
      isSignup = true }
  |> Handler.map Response.ofHtml
  |> Handler.flatten
  |> get "/user/signup"

let private signupPostEndpoint viewContext =
  handler {
    let! signupData =
      getLoginData "/user/signup" viewContext
      |> Handler.bind (fun loginData ->
        match loginData with
        | LoginFormData d -> Handler.return' d
        | GoogleRedirectData _ ->
          // Should never be posted to this url
          Handler.failure (
            Response.withStatusCode 500 >> Response.ofEmpty
          ))

    let! user = findUserRecord signupData.username

    match user, signupData.invite with
    | _, None ->
      return!
        userForm
          viewContext
          "Sign up"
          { location = "/user/signup"
            usernameValue = Some signupData.username
            usernameProblem =
              Some "You must supply an invite code"
            passwordValue = Some signupData.password
            passwordProblem =
              Some "You must supply an invite code"
            isSignup = true }
        |> Handler.bind (HxFragment "user-form")
    | Some _, Some "VIS" ->
      return!
        userForm
          viewContext
          "Sign up"
          { location = "/user/signup"
            usernameValue = Some signupData.username
            usernameProblem = Some "Username already taken"
            passwordValue = Some signupData.password
            passwordProblem = None
            isSignup = true }
        |> Handler.bind (HxFragment "user-form")
    | None, Some "VIS" ->
      let userId = System.Guid.NewGuid()

      let userRecord =
        { Id = userId
          Username = signupData.username
          PasswordHash = None
          State = Active
          ExternalInfo = None }

      let passwordHash =
        passwordHasher.HashPassword(
          userRecord,
          signupData.password
        )

      let! _ =
        createUser (
          Created
            { userRecord with
                PasswordHash = Some passwordHash }
        )

      return
        Response.signInOptions
          "Cookies"
          (makePrincipal userRecord)
          (AuthenticationProperties(RedirectUri = "/"))
    | _, Some _ ->
      return!
        userForm
          viewContext
          "Sign up"
          { location = "/user/signup"
            usernameValue = Some signupData.username
            usernameProblem =
              Some "You must supply an invite code"
            passwordValue = Some signupData.password
            passwordProblem =
              Some "You must supply an invite code"
            isSignup = true }
        |> Handler.bind (HxFragment "user-form")
  }
  |> Handler.flatten
  |> post "/user/signup"

let navbarAccountView =
  handler {
    match! getSessionUser with
    | Some user ->
      return
        [ B.navbarItemSpan
            []
            [ B.icon "account" []; _span' user.username ]
          B.navbarItemA
            [ _href_ "/user/logout" ]
            [ _text "Logout" ] ]
    | None ->
      return
        [ B.navbarItemA
            [ _href_ "/user/login" ]
            [ _text "Login" ]
          B.navbarItemA
            [ _href_ "/user/signup" ]
            [ _text "Signup" ] ]
  }

module Service =
  let addService: AddService =
    fun _ sc ->
      sc.ConfigureMarten
        (fun (storeOpts: Marten.StoreOptions) ->
          storeOpts.Projections.Add<UserRecordProjection>
            ProjectionLifecycle.Inline
          |> ignore)


  let endpoints viewContext =
    [ loginGetEndpoint viewContext
      loginPostEndpoint viewContext
      logoutEndpoint
      signupGetEndpoint viewContext
      signupPostEndpoint viewContext ]
