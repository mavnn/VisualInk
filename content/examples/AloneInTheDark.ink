#title Alone in the dark
// Set up
VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""
// End set up

~scene = "Cafe night"

Once upon a time...

...late at night...

~speaker = "Eddy"

Wow, it feels a bit strange in here with <>
no power or lights. #emote suspicious

* [I'll just wait here a moment] -> wait
* [Let me see if I can find a torch] -> torch

=== wait ===
-> surprise

=== torch ===

Ah, that's great! Now I can see what's going on! #emote smile

-> surprise

=== surprise ===

~speaker = "Narrator"

Suddenly Eddy heard a loud {~crash|smash|crunch}!

~speaker = "Eddy"

{ - wait: 
    What's that?! #emote fear #animation shake

    Oh. Phew. It's just Moget the cat.
    
  - else:
    Moget, you clumsy cat! What a mess! #emote rage  #animation shake
}

Oh well. Better get on with cleaning up.

-> END
