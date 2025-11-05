// <- Lines starting like this are comments.
//    These aren't part of your visual novel,
//    they're a note you can leave for yourself.

// We use 3 special variables in our visual novels.
// These store who's speaking, what scene is showing,
// and what music is playing. We can leave them blank
// if there's no music or location yet.
VAR scene = ""
VAR music = ""

// We normally start the speaker set to the special
// "Narrator" value, which allows us to add narration
// to our story (narration is all the bits of a story
// that are told to the audience, but none of the
// characters actually say!)
VAR speaker = "Narrator"

// Normal text like this is our 'script' -
// what the player sees. So this is our
// opening narration.
Once upon a time...

// Let's set the scene
~speaker = "Elly"
~scene = "Cafe"

Hey, Eddy. We should really get going for school.

~speaker = "Eddy"

// See that '#emote' at the end? That changes
// the emotion of the speaker shown. Remember
// that you need an image to match!
I dunno. I don't really feel like it. #emote tired

// This block creates a set of choices; the lines
// starting with a '*' are each a choice, while
// the '-> name' bit says 'go to the section with
// that name'
 * [But I suppose we should] -> school
 * [Let's just stay here] -> noschool


// Talking of sections! here's are first one.
=== school ===
// Change the scene, but Eddy is still talking.
~scene = "Classroom"

Actually, this new history teacher is pretty interesting!

// Sections have to end by going somewhere.
-> evening

=== noschool ===
~speaker = "Narrator"
... some time later ...

~speaker = "Eddy"
I think I've drunk too much coffee! #emote fear

-> evening

=== evening ===
~scene = "Cafe"

~speaker = "Elly"
So, how was your day?

~speaker = "Eddy"
// This is "conditional text"
{
  // This line will be used if you've seen the "school" section
  - school: Pretty good, actually! #emote smile
  // This line will be used if you've seen the "noschool" section
  - noschool: Terrible! I can't stop twitching #emote fear
}

Tomorrow, I'm going to school! #emote driven

// As we said - sections have to end by going somewhere,
// but one of the places they  can go is 'END' - which
// always ends the story
-> END
