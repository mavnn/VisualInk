# The Visual Ink cheat sheet

Trying to remember how to get Ink to do that one particular thing? Not sure if `+` or `*` is the choice type that
disappears if you've seen it before? Here's a whistle stop tour of reminders for the most common Ink features.

## Dialogue

To display a line of dialogue, just add that text to your script.

<ink-element>

```ink
Say hello!

Say goodbye!
```

</ink-element>

That is actually a complete Visual Ink "novel", which will show a speech bubble with "Say hello!", wait for the player
to click, and then show a speech bubble with "Say goodbye!" (and a "The End" button).

Lines with `//` at the beginning are ignored, and are useful for you to add notes to yourself in your script.

<ink-element>

```
Say hello!

// TODO: add a novel

Say goodbye!
```

</ink-element>

This produces exactly the same game as above.

Normally though, you'll see Visual Ink novels start with some variables:

<ink-element>

```
// Setup
VAR speaker = "Narrator"
VAR music = ""
VAR scene = ""
// End setup
```

</ink-element>

Let's see why!

## Variables

You can only use variables that you have declared with the `VAR` keyword, and you can only declare a variable once.

To change the value stored in a variable, we use the `~` operator.

<ink-element>

```
VAR speaker = "Narrator"

VAR speaker = "Bob" // error!
~speaker = "Bob" // changes the speaker
```

</ink-element>

The `speaker`, `scene`, and `music` variables are special variables that control the sound and images displayed by
Visual Ink. Any other variables are all yours!

You can use the "Change" buttons at the top of the editor to automatically add a line in your script changing
the speaker, scene, or music to something that exists in your library. Don't have the thing you need in your library?
You can always upload more by going to the relevant section in the main menu bar.

It can be really helpful to add variables to track things like money, resources, or hit points depending on the
type of story you're writing.

Useful quick tip: if you're keeping track of a number that goes up or down by one, we do that so often there's
a special "cheat" way of doing it.

<ink-element>

```
VAR hit_points = 20

// Long hand version
~hit_points = hit_points - 1
~hit_points = hit_points + 1

// Short cheat version
~hit_points-- // down one
~hit_points++ // up one
```

</ink-element>

## Tags

When you change a variable, it stays changed until you alter it again. But you can also "tag" a line of text, like this:

<ink-element>

```
What was that?! #emote fear #sfx Dun dun dah
```

</ink-element>

Tags only apply to the line of text they are at the end of. You can tag a line with anything, but there are a few special
tags in Visual Ink:

### show
Override which character is shown. 

<ink-element>

```
~speaker = "Narrator"
Bob was scared #show Bob
``` 

</ink-element>

This will show the picture of Bob even through the speech bubble at the top will still be the narrator.

### emote

Change which emotion the character is showing for this line. You can see what emotions you have images
for in the [Speakers section](/assets/speakers).

For example: `Aaaaargh! #emote fear` will show the image of the current speaker's "fear" emote.

### vo

The "voice over" tag doesn't show any characters at all, but still shows who is speaking in the speech
bubble at the top. This is for situations where the character speaking is looking at the scene.

### vfx

A visual effect; at the moment there is only one: shake.

<ink-element>

```
Ow! That hurt! #vfx shake
```

</ink-element>

### sfx

A sound effect. If you set the `music` variable, the background music will continue playing in a loop in
the background. If you tag a line with this tag, it will play the named piece of "music" from your music
collection once and then stop.

This is very useful if you want either sound effects or if you want produce an audio version of your visual
novel.

<ink-element>

```
Good morning, Chris! #sfx Voice saying good morning
```

</ink-element>

### Combining tags

You can apply as many tags as you want to the same line:

<ink-element>

```
~speaker = "Narrator"
Chris was biffed upon the nose. #show Chris #vfx shake #sfx punch #emote surprise
```

</ink-element>

Word of warning: beware of using tags and the glue operator (`<>`) that combines lines together.

<ink-element>

```
~speaker = "Eddy"

Wait... you did what to my coffee? <> #emote fear

Arrrrrgh! #emote rage
```

</ink-element>

Because the tag applies to the whole line that will be shown, one of the emotes will be ignored.

## Knots and diverts

A "knot" is one of the sections of your story that can be diverted to.

<ink-element>

```
=== this_is_a_knot ===

Some story here.

-> END
```

</ink-element>

The `->` arrow moves the story to a new location when the story gets to it - specifically, the location named after the arrow.
Every knot must end every branch with a divert to somewhere; if the branch leads to the story ending, use the special target
`END` as shown above to let Ink know that this is a place where the game can finish.

## Choices

There are two main types of choice:

<ink-element>

```
Some story

+ This choice will be shown every time the story gets here
* This choice will only be shown until it has been picked, then it will disappear
```

</ink-element>

You may have noticed that the text in your choice gets repeated as the next line in the story when you choose it. You can
stop that from happening by wrapping the choice text in square brackets `[`/`]`:

<ink-element>

```
Some story

+ This text will be shown in the choice button, and in the next screen -> go_on
+ [This text will only be shown in the choice button] -> go_on
```

</ink-element>

Apart from choices that disappear when chosen, you can also make them appear or not appear depending on the value
of variables or the knots that the player has visited:

<ink-element>

```
VAR hit_points = 20

Bobyt the goblin punches you on the nose.
~hit_points--

// Will show up if you have less than 20 hit points
+ {hit_points < 20} Drink a healing potion -> healing
// Will show up if you've visited the "befriended_goblins" knot
+ {befriended_goblins} Ow! Bobyt, it's me! -> complain_to_chief
```

</ink-element>

## Dynamic text

You can vary text each time it is seen in several different ways.

You can progress through several options, repeating the last one on further visits:

<ink-element>

```
Hi! {First time here?|Good to see you again!|I'll grab you the usual.}
```

</ink-element>

You can select text at random from a selection (instant madlibs!):

<ink-element>

```
You {~dodge|block|duck} the {~kick|punch|headbut} from the...
```

</ink-element>

You can cycle through a list, repeating it forever:

<ink-element>

```
=== start_of_school_day ===
It's {&Monday|Tuesday|Wednesday|Thursday|Friday} morning and
```

</ink-element>

You can even put dynamic text **in** dynamic text!

<ink-element>

```
{~you're {~two|three|ten} minutes late|you've got {~two|three} minutes spare}.
```

</ink-element>

## Advanced features

With the features above, you can already write stories with flexible outcomes, and
where decisions earlier on can affect what happens later. But once you've got
confident with these beginnings, we can also use some more advanced features
to build more complex branching, "sub-stories" that can appear in multiple places
in the novel, and we can add actual game mechanics such as random chances of success
or failure.

We'll be adding more examples here as the site grows, but if you don't want to wait the
company who created Ink also created the [Writing With Ink](https://github.com/inkle/ink/blob/master/Documentation/WritingWithInk.md) guide that covers everything in the language. It's quite dense, but it really does
cover the whole thing.
