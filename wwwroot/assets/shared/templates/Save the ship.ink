VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""
VAR crew_members_helped = false
VAR traitor_discovered = false
VAR fuel_found = false
VAR piloting_done = true

// Main story
~speaker = "Narrator"
~scene = "Spaceship"
Well, it seems like everything is going wrong.

-> start

=== start

~speaker = "Narrator"
~scene = "Spaceship"

What do you want to try and deal with {first|next}?

* [The fuel leak] -> fuel -> start
* [The falling into a planet] -> piloting -> start
* [The injuries to the crew] -> help_crew -> start
* [Investigate the suspicious lead up to the accident] -> discover_traitor -> start
+ [I think I've done everything I can!] -> ending

=== ending
~speaker = "Narrator"
{ piloting_done and fuel_found and crew_members_helped and traitor_discovered: 
  Well done! The ship is saved. 
- else: Oops. The GSV Badly Sketched appears to have been lost with all hands.
}

-> END

/// Help the crew
=== help_crew ===
~scene = "Spaceship corridor"

~speaker = "Eddy"
Ow! That really hurts! #emote fear

+ [Stick a plaster on]
  Incredible!
  ~crew_members_helped = true 
  ->->
+ [Faint at the sight of blood]
  Aaaargh! What are you doing? #emote rage
  ~crew_members_helped = false
  ->->

=== discover_traitor ===
~scene = "Spaceship corridor"

~speaker = "Mr. Noone"
I am a totally normal member of the crew! #emote disguised

Look over there! #emote disguised

+ [Grab the hat]
  I would have got away with it except for you meddling kids!
  ~traitor_discovered = true 
  ->->
+ [Look over there]
  Muhahahaha! #vo
  ~traitor_discovered = false
  ->->

=== fuel ===
~scene = "Spaceship engine room"

~speaker = "Eddy"
Oh no! We're running out of fuel! #emote fear

+ [Look behind the sofa]
  Incredible!
  ~fuel_found = true 
  ->->
+ [Run in circles, scream and shout]
  Aaaargh! What are you doing? #emote rage
  ~fuel_found = false
  ->->

=== piloting ===
// The cockpit is the default scene in the spaceship
~scene = "Spaceship"

~speaker = "Eddy"
What are we going to do? #emote fear

+ [Perform amazing feats of astrogation]
  Incredible!
  ~piloting_done = true 
  ->->
+ [Push buttons at random]
  Aaaargh! What are you doing? #emote rage
  ~piloting_done = false
  ->->