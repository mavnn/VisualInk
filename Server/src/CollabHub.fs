module VisualInk.Server.CollabHub

open Microsoft.AspNetCore.SignalR

type CollabHub() =
  inherit Hub()

  member x.CreateGroup() =
    x.Groups.AddToGroupAsync(x.Context.ConnectionId, x.Context.ConnectionId)

  member x.RequestDocument(groupName: string) =
    task {
      do! x.Groups.AddToGroupAsync(x.Context.ConnectionId, groupName)

      do!
        x.Clients
          .Client(groupName)
          .SendAsync("RequestDocument", x.Context.ConnectionId)
    }

  member x.DocumentRequested
    (
      connectionId: string,
      info: obj
    ) =
    x.Clients.Client(connectionId).SendAsync("DocumentRequested", info)

  member x.PushUpdates(groupName: string, version: int, updates: obj array) =
    x.Clients.Client(groupName).SendAsync("PushUpdates", version, updates)

  member x.UpdateBroadcast(groupName: string, version: int, update: obj) =
    x.Clients.Group(groupName).SendAsync("UpdateBroadcast", version, update)
