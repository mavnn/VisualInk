// Set up
VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""
// End set up

-> start

=== start ===
~speaker = "Eddy"
~scene = "Cafe"

'morning! #emote smile

Can I have a plain black coffee, please?

-> make_coffee("Eddy") ->

Eddy is waiting for you as you finish off.

~speaker = "Eddy"
{ 
- make_coffee.good_coffee:
  That's some great coffee! #emote laugh
- make_coffee.bad_coffee:
  What's that supposed to be!? #emote rage
- else:
  Did you... not get my order? #emote suspicious
}

~speaker = "Elly"
I'd like a coffee please!

-> make_coffee("Elly") ->

You turn back to Elly waiting at the bar.

~speaker = "Elly"
Thank you!

-> END

=== make_coffee(customer) ===
~speaker = "Narrator"
You {start|continue} making {customer}'s coffee

* {not grind} [Add the beans to the french press] 
  -> add_beans(customer)
* [Grind the beans] 
  -> grind(customer)
* [Add boiling water to the french press]
  -> add_water(customer)
* {grind} [Add the grinds to the french press]
  -> add_grinds(customer)
+ [Give up] ->->
+ {good_coffee} [You make another great coffee] ->->

= grind(customer)

There's a loud noise as the beans are reduced to dust.

-> make_coffee(customer)

= add_water(customer)
{
  - add_grinds:
    The water swirls the ground coffee <>
    and the liquid starts to burn a rich <>
    brown colour. -> good_coffee
  - add_beans and not add_grinds:
    The beans float and bump around in the <>
    water. The water begins to look like <>
    somebody has washed some dishes in it. -> bad_coffee
  - else:
    -> make_coffee(customer)
}

= add_beans(customer)

{
  - add_water:
    The beans float on top of the water in an <>
    awkward mess. It does not look like coffee.
    -> bad_coffee
  - else:
    The beans sit at the bottom of the press, looking
    lonely.

    After a moment of thought, you take them back out.

    -> make_coffee(customer)
}

= add_grinds(customer)

{
  - add_water:
    The grinds float on top of the water in an <>
    awkward mess. 

    The water slowly turns the colour of dirty washing <>
    up water.

    + [Stir wildly] With some hard work, you save the coffee
      -> good_coffee
    + [Give up and pretend nothing happened]
      ->->
  - else:
    The grinds sit at the bottom of the press, looking
    lonely.
    -> make_coffee(customer)
}

= good_coffee

->->

= bad_coffee

->->
