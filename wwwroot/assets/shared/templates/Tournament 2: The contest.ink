INCLUDE Tournament 1: The sensei

LIST rank = 
  feeble, poor, reasonable, 
  good, excellent, remarkable, 
  fantastic, incredible, spectacular, 
  amazing, monstrous, awesome

LIST opponents = Eddy, Elly
VAR remaining_opponents = (Eddy, Elly)
VAR opponents_defeated = ()
// default, but we'll randomize before
// the first battle
VAR current_opponent = Eddy

VAR strength_rank = reasonable
VAR speed_rank = reasonable
VAR skill_rank = reasonable
VAR control_rank = reasonable
VAR honour_rank = reasonable

-> flashbacks -> the_contest

=== the_contest

-> initial_rankings ->

Now, your training is complete. But your sensei is wise, <>
and knows however much he values the teachings of the art <>
a student cannot learn in isolation.

~speaker = "Mr. Noone"

It is time! I have taught you all you can learn without <>
experience.

I have received an invite to the annual Steel Street Fist Kombat <>
and I have submitted your name as my representative.

Go and make the school proud!

~speaker = "Narrator"
~scene = "Cafe"

You have a few months to prepare before the tournament.

-> show_rankings ->

How do you want to spend your time?

* [Hit the gym and bulk up]
  ~strength_rank++
* [Practice plucking roast chestnuts from a fire]
  ~speed_rank++
* [Practice a single kick ten thousand times]
  ~skill_rank++
* [Retreat to mountain solitude and meditate]
  ~control_rank++
* [Continue existing commitments at the school]
  ~honour_rank++
-

You are as prepared as you'll ever be, and the day of the <>
tournament has arrived. Time for your first challenge!

-> next_fight ->

-> ending

=== next_fight
{ LIST_COUNT(remaining_opponents) > 0:
  -> fight
- else:
  ->->
}

=== fight
~current_opponent = LIST_RANDOM(remaining_opponents)
~remaining_opponents -= current_opponent
{ 
 - current_opponent == Elly:
   -> elly_fight -> next_fight
 - current_opponent == Eddy:
   -> eddy_fight -> next_fight
}

=== eddy_fight
You battle Eddy! He looks strong and sneaky. #show Eddy #emote suspicious

~speaker = "Eddy"
I'll take you down! The Mountain Goat School is the strongest! #emote rage

~speaker = "Narrator"
-> choices(0, 1)

= choices(victory_points, rounds)

The fight {starts|continues|draws to a close}.

* {rounds <= 2} [Try and force a win with your strength]
  ~qualities_seen += strength
  ~temp strength_roll = strength_rank + RANDOM(-2, 2)
  The two of you grapple in a show of raw strength, <>
  {
   - strength_roll >= good:
     but it soons become clear that your {strength_roll} display <>
     of power is overcoming Eddy.

     ~speaker = "Eddy"
     Aaaaaaargh! #emote rage
     ~speaker = "Narrator"
     ~victory_points++
   - else:
     but it soon becomes clear Eddy is stronger than you.

     ~speaker = "Eddy"
     Muhahahahah! #emote driven
     ~speaker = "Narrator"
     ~victory_points--
  }
  -> choices(victory_points, rounds+1)
* (eddy_cheats) {rounds <= 2} [Watch Eddy careful as you circle]
  ~qualities_seen += control

  For a split second, the referee's view of Eddy is hidden <>
  by your body as the two of you circle. Eddy springs forward <>
  and you spot the gleam of an illegal weapon hidden in his <>
  glove.

  If you win, the evidence will be there to clear you regardless <>
  of how you win. If you don't, Eddy is sure to hide the blade.

  In an instant, you have to decide...

  ** [Use your speed to land an underhanded blow first]
    ~qualities_seen -= honour
    ~qualities_seen += speed
    ~temp speed_roll = speed_rank + RANDOM(-2, 2)
    {
     - speed_roll >= reasonable:
       With {speed_roll} speed you snap a painful <>
       blow to a nerve cluster before grabbing his glove <>
       and showing it to the ref.
       ~victory_points = 1
       -> finish(victory_points)
     - else:
       Your block is {speed_roll}, and the blade catches you, <>
       splitting your skin only slightly... but you can feel <>
       some kind of poison at work, making you feel weaker.
       ~victory_points--
       ~strength_rank--
    }
  ** [Use your skill to deflect the blow without taking a cut]
    ~qualities_seen += skill
    ~temp skill_roll = skill_rank + RANDOM(-2, 2)
    {
     - skill_roll >= good:
       With {skill_roll} skill you deflect the blow.  <>
       Then in Eddy's distraction trying to make sure <>
       the ref doesn't spot his cheating, <>
       you land a solid counter blow of your own.
       ~victory_points++
     - else:
       You're block isn't precise enough, and the blade catches <>
       you, splitting your skin. You can feel some kind <>
       of poison at work, making you feel weaker.
       ~victory_points--
       ~strength_rank--
    }
  --
  -> choices(victory_points, rounds+1)
* {rounds <= 2} [Launch a complex assault]
  ~qualities_seen += skill
  It looks like Eddy relies on sheer power over skill, <>
  so you decide to overwhelm him with a barrage of unusual <>
  techniques he doesn't know how to counter.
  {
   - skill_rank + RANDOM(-2, 2) >= reasonable:
     It works! You hand blow after blow on your confused <>
     opponent!

     ~speaker = "Eddy"
     Aaaaaaargh! #emote rage
     ~speaker = "Narrator"
     ~victory_points++
   - else:
     Unfortunately, Eddy is just too strong and tough; <>
     your blows are landing, but he doesn't seem to care and <>
     throws you hard onto the ground.
     ~speaker = "Eddy"
     Muhahahahah! #emote driven
     ~speaker = "Narrator"
     ~victory_points--
  }
  -> choices(victory_points, rounds+1)
* {rounds <= 2} {RANDOM(1, 2) == 1} [Eddy slips, and you...]
  ** [...calmly wait for him to recover]
    ~qualities_seen += honour
  ** [...catch the advantage and strike!]
    ~victory_points++
  --
  -> choices(victory_points, rounds+1)
* -> finish(victory_points)

= finish(victory_points)

With a loud ring of the bell, the fight is ended.

{ 
  - victory_points > 0:
    { 
      - eddy_cheats: You not only defeat Eddy, but reveal his treachery.
      - else: You soundly defeat Eddy!
    }
    ~opponents_defeated += Eddy
  - victory_points == 0:
    At the end of the fight, the judges rule it was a draw.
  - else:
    { control_rank + RANDOM(-2, 2) >= good:
      You lost the fight. But with perfect control you accept <>
      the outcome and it is clear to all around you that you <>
      will learn from it.
    - else:
      You know that you came here to do your best, not to "win" <>
      but you can't help but let the disappointment leak out <>
      as you lose the fight.
    }
}

->->

=== elly_fight
You battle Elly! She looks quick and friendly. #show Elly #emote smile

~speaker = "Elly"
Let's see who's school is stronger!
~speaker = "Narrator"

-> choices(0, 1)

= choices(victory_points, rounds)
The fight {starts|continues|draws to a close}.

* {rounds <= 2} [Try and force a win with your strength]
  ~qualities_seen += strength
  The two of you grapple in a show of raw strength, <>
  {
   - strength_rank + RANDOM(-2, 2) >= reasonable:
     but it soons become clear that you're stronger than Elly.

     ~speaker = "Elly"
     Ooof! #emote fear
     ~speaker = "Narrator"
     ~victory_points++
   - else:
     but it soon becomes clear Elly is stronger than you.

     ~speaker = "Elly"
     Surprise! #emote laugh
     ~speaker = "Narrator"
     ~victory_points--
  }
  -> choices(victory_points, rounds+1)
* {rounds <= 2} [Watch Elly careful as you circle]
  ~qualities_seen += control

  You and Elly circle each other for a moment, and then <>
  suddenly Elly burst forwards!

  In an instant, you have to decide...

  ** [Match her speed for speed, trusting your reactions]
    ~qualities_seen += speed
    {
     - speed_rank + RANDOM(-2, 2) >= good:
       Fast as Elly is, you're faster and you intercept her <>
       attack with one of your own.
       ~victory_points++
     - else:
       You're too slow, and Elly darts in and out of your range <>
       landing several painful blows.
       ~victory_points--
    }
  ** [Trust your body to absorb Elly's fast but weak attacks]
    ~qualities_seen += strength
    {
     - strength_rank + RANDOM(-2, 2) >= good:
       Elly lands several blows on you, but you take them all <>
       in stride. Before she can move back, you counter.
       ~victory_points++
     - else:
       Elly may not be the biggest fighter, but she's not weak <>
       either. You quickly regret your decision not to defend <>
       more carefully as she pummels you.
       ~victory_points--
    }
  --
  -> choices(victory_points, rounds + 1)
* {rounds <= 2} [Launch a complex assault]
  ~qualities_seen += skill
  It looks like Elly is both fast and skilled - it will be <>
  hard to overwhelm her with just technique, but that isn't <>
  going to stop you trying!
  {
   - skill_rank + RANDOM(-2, 2) >= excellent:
     It works! You hand blow after blow on your confused <>
     opponent!

     ~speaker = "Elly"
     Ooof! #emote fear
     ~speaker = "Narrator"
     ~victory_points++
   - else:
     Unfortunately, Elly is just too skilled and quick; <>
     it feels like you're trying to hit a ghost until a <>
     rather solid counter blow sneaks past your flurry.
     ~speaker = "Elly"
     My turn! #emote laugh
     ~speaker = "Narrator"
     ~victory_points--
  }
  -> choices(victory_points, rounds+1)
* {rounds <= 2} {RANDOM(1, 2) == 1} [Elly slips, and you...]
  ** [...calmly wait for her to recover]
    ~qualities_seen += honour
  ** [...catch the advantage and strike!]
    ~victory_points++
  --
  -> choices(victory_points, rounds + 1)
* -> finish(victory_points)

= finish(victory_points)

With a loud ring of the bell, the fight is ended.

{ 
  - victory_points > 0:
    The crowd cheers as the referee announces your win!
    {qualities_seen ? honour: 
       Elly bows deeply to you

       ~speaker = "Elly"
       Thank you so much for the fight, it was an honour!

       Let's grab a meal after the tournament and share some <>
       techniques! #emote smile
       ~speaker = "Narrator"
    }
    ~opponents_defeated += Elly
  - victory_points == 0:
    At the end of the fight, the judges rule it was a draw.
  - else:
    { control_rank + RANDOM(-2, 2) >= good:
      You lost the fight. But with perfect control you accept <>
      the outcome and it is clear to all around you that you <>
      will learn from it.
    - else:
      You know that you came here to do your best, not to "win" <>
      but you can't help but let the disappointment leak out <>
      as you lose the fight.
    }
}

->->

=== ending

You return to the school after your adventure.

~speaker = "Mr. Noone"
{
  - qualities_seen ? most_important:
    You have displayed {most_important}, my student. Excellent!
  - else:
    ~speaker = "Mr. Noone"
    Have I taught you so badly that you showed no {most_important}?!
    ~speaker = "Narrator"
}

{
  - LIST_COUNT(qualities_seen ^ qualities_valued) >= 2:
    Whatever else has happened, you have shown most of the qualities <>
    we value. Well done, my student.
  - LIST_COUNT(qualities_seen ^ qualities_valued) >= 1:
    I'm glad you have learned something from our teachings, but <>
    there is more to be done before you are ready to become a master.
  - else:
    You have learned nothing from me, and disgraced the school. Leave now.
    -> END
}

{
  - LIST_COUNT(opponents_defeated) == 2:
    And you also brought fame and honour to the school by <>
    winning the tournament!
  - else:
    You may not have triumphed... yet. But your values are true, <>
    and we have not finished our work yet.
}

~speaker = "Narrator"

Story state:

-> show_rankings ->

You defeated {opponents_defeated}.

You displayed {qualities_seen}, while your sensei values {qualities_valued}.

-> END

=== initial_rankings

// Set up rankings based on your background
{
  - most_important == strength:
    ~strength_rank += 2
  - qualities_valued ^ strength:
    ~strength_rank += 1
}

{
  - most_important == speed:
    ~speed_rank += 2
  - qualities_valued ^ speed:
    ~speed_rank += 1
}

{
  - most_important == skill:
    ~skill_rank += 2
  - qualities_valued ^ skill:
    ~skill_rank += 1
}

{
  - most_important == control:
    ~control_rank += 2
  - qualities_valued ^ control:
    ~control_rank += 1
}

{
  - most_important == honour:
    ~honour_rank += 2
  - qualities_valued ^ honour:
    ~honour_rank += 1
}
//

->->

=== show_rankings

You have {strength_rank} strength, {speed_rank} speed, <>
and {skill_rank} technical skills. Your self-control is <>
{control_rank} and your sense of honour is {honour_rank}.

->->
