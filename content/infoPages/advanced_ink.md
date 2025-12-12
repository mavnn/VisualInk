# Advanced Ink

If you're new to ink, you probably want to check out our [introduction first](/info/ink_guide). With that out of the way, _this_ page focuses on some features of Ink that can be very helpful as your game gets more complex - but which can also be a bit more confusing.

## Gathers

Diverts and knots are great for branching your story, but can become very verbose if you have choice points where you branch out for a quick decision (which you may record for later) but then they all continue on a the same point.

Ink has a special syntax for making this easier, the "gather".

<ink-element>

```ink
Oh, so you pick the first choice a lot do you?

* A choice
  ~choice_one_picked++
* A different choice
-

More story
```

</ink-element>

See that `-` at the end of the choices? That's the gather; it is basically the same as doing:

<ink-element>

```ink
Oh, so you pick the first choice a lot do you?

* A choice
  ~choice_one_picked++
  -> next_bit
* A different choice
  -> next_bit

=== next_bit
More story
```

</ink-element>

You can also put gathers _in_ gathers by doubling up the choice marker and the matching gather marker, but it is probably better not to take that too far:

<ink-element>

```ink

Oh, so you pick the first choice a lot do you?

* A choice
  ~choice_one_picked++
  Bet you don't do it again!
  ** Every time
     ~choice_one_picked++
  ** Nah
  --
* A different choice
-
```

</ink-element>

## Knots with arguments

Sometimes you want to reuse a knot in several places, with only a small detail changed. Maybe you can be given a piece of news
by several different characters, or maybe an event can happen several times but where the story continues afterwards can vary.

This can be achieved by creating a knot that can take "arguments" - extra pieces of information you must supply when you divert to it.

Maybe you catch the bus to school every day, but depending on the day of the week what happens when you arrive changes:

<ink-element>

```ink
=== bus_trip(-> continue)
You chat to your friends on the bus.

// conversation here

-> continue

=== monday_breakfast
You catch the bus to school!

-> bus_trip(-> monday_school)


=== tuesday_breakfast
You just make it to the bus in time.

-> bus_trip(-> tuesday_school)
```

</ink-element>

Or maybe you just want to pass in a name:

<ink-element>

```ink
=== say_hello(name)
Hello! I'm {name}
-> END

=== meet_bob
-> say_hello("Bob")
```

</ink-element>

## Tunnels

Talking about reusable knots, or knots that can appear in different places, we have one special case: knots that (normally!) return you back to where you came from. One really common use case for these "tunnel" knots are for things like checking what you're carrying, or your current health - things that will happen again and again during a play through but don't change the direction of the story.

These are both diverted to, and diverted back from, with a special syntax:

<ink-element>

```ink
=== show_rankings

You have {strength_rank} strength, {speed_rank} speed, <>
and {skill_rank} technical skills. Your self-control is <>
{control_rank} and your sense of honour is {honour_rank}.

->->

=== story_stuff

* [A thing to do next] -> next_thing
* [Do nothing]
* [Check your rankings] -> show_rankings ->
-
```

</ink-element>

That trailing `->` on the last choice is saying "show_rankings is a tunnel, and we'll carry on here". And in `show_ranking` itself you can see that we have access to a special double divert `->->` that only exists in tunnels and means "go back to the place marked for carrying on".

## Stitches

A quick but useful thing: a stitch is like a knot, but inside another knot. Sounds a bit useless, but it allows you to give short names to things that would otherwise clash. Let's say that you want each school day in your novel to have events during the first period.

<ink-element>

```ink
=== monday_school
-> first_period

= first_period
Stuff happens!
-> END

=== tuesday_school
-> first_period

= first_period
Different stuff happens
-> END
```

</ink-element>

Knot names have to be unique throughout your story, so without stitches you'd need to have `monday_first_period` and so on - but stitches only need to have a unique name within their knot.

## Stacks

(Warning: these are a special feature of VisualInk - they won't work if you copy your Ink script to other platforms)

Stacks allow you to create a "stack" of a set size that you can push things into and out of. Useful for representing things like
cards in a card game.

You declare a stack using the `STACK` keyword, and it then provides you with ways to push items into the stack, pop items out of the stack, or look up/set the items by index (i.e. index 1 is the first item in the stack, etc).

I'll leave a brief example here, and you can read more about them in a [blog post](https://blog.mavnn.co.uk/2025/12/12/stacks_in_ink.html) from when they were introduced.

<ink-element>

```ink
STACK cards 2 false

-> draw_card

=== draw_card
~temp did_draw = cards_push(RANDOM(1, 10))
{ did_draw:
  You now have {cards_last} cards in your hand
  -> draw_card
- else:
  Your hand is full
  -> play_card
}

=== play_card
~temp next_card = cards_pop()
{ next_card == false:
  You're out of cards!
  -> END
- else:
  You play a {next_card}.
  -> play_card
}

// Draws two cards and then plays two cards
```

</ink-element>

## Want to take it further?

We'll be adding more examples here as the site grows, but if you don't want to wait the
company who created Ink also created the [Writing With Ink](https://github.com/inkle/ink/blob/master/Documentation/WritingWithInk.md) guide that covers everything in the language. It's quite dense, but it really does
cover the whole thing. In particular, lists are really useful in Ink and the examples in
the guide will give you some great ideas for using them.
