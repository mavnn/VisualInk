// Set up
VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""

LIST qualities =
  strength, skill, control, 
  speed, honour

VAR qualities_seen = ()
VAR qualities_valued = ()

VAR most_important = honour
VAR important = honour
VAR least_important = honour
// End set up

=== flashbacks ===
~scene = "Classroom afternoon"

Your sensei stands at the front of the room as you come in.

Years of training has led to this moment, and as you take the <>
last few steps of this years long journey the memories flood <>
through you.

After you started showing promise, sensei started <>
sharing his most fundamental training.

-> choose_flashback(most_important) ->

Of course, it didn't stop there. It turned out you were a natural; <>
the fastest learning student sensei had ever taught.

As the fundamentals solidified, he moved onto the second most <>
important pillar.

-> choose_flashback(important) ->

Only recently, as he prepared to <>
send you out to travel the world and met other students and styles <>
did he let you spend any real time on the supporting aspects of <>
his art.

-> choose_flashback(least_important) ->

Your sensei believes nothing is more important than <>
{most_important}, thinks {important} is important <>
and values {least_important}.

->->

=== choose_flashback(ref importance)

* [Raw physical strength] -> raw_strength(importance) ->
* [Sheer speed] -> sheer_speed(importance) ->
* [Mental control] -> mental_control(importance) ->
* [Skills and techniques] -> skills(importance) ->
* [Honour and character] -> honour_and_character(importance) ->
-

->->

=== raw_strength(ref importance)
The weight lifting was intense, and soon your strikes were <>
bouncing even the heaviest bags around the gym.

~importance = strength
~qualities_valued += strength

->->

=== sheer_speed(ref importance)
You'd never seen someone move so fast as <>
the training attacks he forced you to block and dodge.

~importance = speed
~qualities_valued += speed

->->

=== mental_control(ref importance)

Sensei had you stand still on one leg for hours, allowed other <>
students to harrass and belittle you, and led you in hours of <>
meditation.

Calm and confidence built themselves deep into your character.

~importance = control
~qualities_valued += control

->->

=== skills(ref importance)
~speaker = "Mr. Noone"
There is no invincible attack, just as there is no perfect defense.

~speaker = "Narrator"
Refusing to leave gaps in your knowledge, sensei drilled you <>
for hours on the techniques of the art, of which defenses worked <>
against which attacks and why.

~importance = skill
~qualities_valued += skill

->->

=== honour_and_character(ref importance)

The common image of martial arts you grew up with as a child <>
always focussed on the physical and the dramatic.

But sensei spent many hours teaching you philosophy, explaining <>
the history of the art, and challenging you with both hypothetical <>
and practical choices designed to reveal your character.

~speaker = "Mr. Noone"
Ability without honor has no value. Worse; it is dangerous, <>
a handing of power to one who should not wield it.

~speaker = "Narrator"

~importance = honour
~qualities_valued += honour

->->
