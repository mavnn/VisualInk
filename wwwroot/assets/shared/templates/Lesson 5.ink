// Set up
VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""
VAR hit_points = 10
// End set up

-> start

=== start ===
You are biffed upon the nose.

~hit_points = hit_points - 6

{ hit_points > 0:
  Ow. That hurt.
  -> check_health -> start

- else:
  Everything fades to black...
  -> END
}

-> END.

=== check_health ===

You have {hit_points} hit points remaining.

->->
