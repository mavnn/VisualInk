// <- Lines starting like this are comments.
//    These aren't part of your visual novel,
//    they're a note you can leave for yourself.

// Set up
VAR scene = ""
VAR music = ""
VAR speaker = "Narrator"
// End set up

Once upon a time...

// Let's set the scene
~speaker = "Elly"
~scene = "Cafe"

Hey, Eddy. We should really get going for school.

~speaker = "Eddy"

I dunno. I don't really feel like it. #emote tired

// These choices will jump you forward through the
// story!
 * [But I suppose we should] -> school
 * [Let's just stay here] -> noschool


=== school ===

~scene = "Classroom"

Actually, this new history teacher is pretty interesting!

// Sections have to end by going somewhere.
-> evening

=== noschool ===
~speaker = "Narrator"

... some time later ...

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
