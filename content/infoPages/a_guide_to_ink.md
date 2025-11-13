# The Ink cheat sheet

Trying to remember how to get Ink to do that one particular thing? Not sure if `+` or `*` is the choice type that
disappears if you've seen it before? Here's a whistle stop tour of reminders.

## Variables

You can only use variables that you have declared with the `VAR` keyword, and you can only declare a variable once.

To change the value stored in a variable, we use the `~` operator.

```
VAR speaker = "Narrator"

VAR speaker = "Bob" // error!
~speaker = "Bob" // changes the speaker
```

The `speaker`, `scene`, and `music` variables are special variables that control the sound and images displayed by
Visual Ink. Any other variables are all yours!

Useful quick tip: if you're keeping track of a number that goes up or down by one, that's so common there's a special
way of doing it.

```
VAR hit_points = 20

// Long hand version
~hit_points = hit_points - 1
~hit_points = hit_points + 1

// Short cheat version
~hit_points-- // down one
~hit_points++ // up one
```

## Tags and emotions

You can tag a line of dialogue, most commonly in VisualInk by using the `#emote` tag. This changes the emotion a character
is showing. Remember to check the sidebar for which emotions you have speaker images for.

```
~speaker = "Eddy"

Wait... you did what to my coffee?

Arrrrrgh! #emote rage
```

Word of warning: beware of using tags and the glue operator (`<>`) that combines lines together.

```
~speaker = "Eddy"

Wait... you did what to my coffee? <> #emote fear

Arrrrrgh! #emote rage
```

Because the tag applies to the whole line that will be shown, one of the emotes will be ignored.

## Knots and diverts

A "knot" is one of the sections of your story that can be diverted to.

```
=== this_is_a_knot ===

Some story here.

-> END
```

The `->` arrow moves the story to a new location when the story gets to it - specifically, the location named after the arrow.
Every knot must end every branch with a divert to somewhere; if the branch leads to the story ending, use the special target
`END` as shown above to let Ink know that this is a place where the game can finish.

## Choices

There are two main types of choice:

```
Some story

+ This choice will be shown every time the story gets here
* This choice will only be shown until it has been picked, then it will disappear
```

You may have noticed that the text in your choice gets repeated as the next line in the story when you choose it. You can
stop that from happening by wrapping the choice text in square brackets `[`/`]`:

```
Some story

+ This text will be shown in the choice button, and in the next screen -> go_on
+ [This text will only be shown in the choice button] -> go_on
```

Apart from choices that disappear when chosen, you can also make them appear or not appear depending on the value
of variables or the knots that the player has visited:

```
VAR hit_points = 20

Bobyt the goblin punches you on the nose.
~hit_points--

// Will show up if you have less than 20 hit points
+ {hit_points < 20} Drink a healing potion -> healing
// Will show up if you've visited the "befriended_goblins" knot
+ {befriended_goblins} Ow! Bobyt, it's me! -> complain_to_chief
```

## Dynamic text

You can vary text each time it is seen in several different ways.

You can progress through several options, repeating the last one on further visits:

```
Hi! {First time here?|Good to see you again!|I'll grab you the usual.}
```

You can select text at random from a selection (instant madlibs!):

```
You {~dodge|block|duck} the {~kick|punch|headbut} from the...
```

You can cycle through a list, repeating it forever:

```
=== start_of_school_day ===
It's {Monday|Tuesday|Wednesday|Thursday|Friday} morning and <>
```

You can even put dynamic text **in** dynamic text!

```
{~you're {~two|three|ten} minutes late|you've got {~two|three} minutes spare}.
```
