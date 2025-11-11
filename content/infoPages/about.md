# About Visual Ink

Visual Ink is a project started by [Michael Newton](https://blog.mavnn.co.uk/), an IT industry veteran and long time interactive story teller (mostly via table top roleplaying games). You can see the code that runs this server or even help to improve the project (whether that's by coding, providing art assets, or helping write the tutorials provided) at the project's [GitHub repository](https://github.com/mavnn/VisualInk).

But this is very much a project built on the generosity of a number of open source and royalty free projects that make running a website like this for education feasible. Here's the main people you should thank for this project existing:

- [Ink](https://www.inklestudios.com/ink/) from Inkle Studios is a production grade domain specific language for writing narrative in games. The scripts you write in Visual Ink are just Ink files, and it's an amazing resource for them to be giving away freely to encourage other storytellers given it is the tool they use themselves to build their own games.
- Liborio Conti provides [No Copyright Music](https://www.no-copyright-music.com/) (in name, and in fact). All of the shared background music assets available when you first login come from Liborio.
- [OpenPeeps](https://www.openpeeps.com/) by Pablo Stanley give us our pre-provided character images to let you get started straight away on writing stories.
- Many members of the visual novel writing community have made assets available for free, including [CHIBI](https://itch.io/queue/c/4113937/visual-novel-backgrounds?game_id=1397476) who provides our default Cafe scene background and Uncle Mugen who created the classroom.
- Finally, on the technical side we lean on many, many, man years of freely provided open source technology. The server is written in [F#](https://fsharp.org/) using the excellent [Falco](https://www.falcoframework.com/) web framework. Your data is being stored in [PostgreSQL](https://www.postgresql.org/) using [Marten](https://martendb.io/), and the website itself is powered by [HTMX](https://htmx.org/) and styled with [Bulma](https://bulma.io/).

These are just the tip from this particular project of the large open source iceberg that supports the web these days. This is your regularly scheduled reminder that if you're using open source commercially, you should really be financially supporting some of the projects that you depend on if you want the whole system to keep on working.

## Other projects to explore

Feeling like you're hitting the limits of Visual Ink? We're not offended! Visual Ink is designed to be an effective way of teaching you to get started in writing your own visual novels, but one of the things that makes it an effective teaching tool is all the things it won't let you do. Sorry!

But first: if you're feeling limited by how complex you can make the actual narrative rather than how it is displayed, check out the full guide to [writing with Ink](https://github.com/inkle/ink/blob/master/Documentation/WritingWithInk.md). We don't introduce all of Ink's features in the template lessons here at Visual Ink, but they do nearly all work here. The only exception is native functions.

If you're feeling constrained by how Visual Ink displays your games and are interested in continuing to use Ink to create interactive stories on websites, Inkle have have a [great tutorial](https://www.inklestudios.com/ink/web-tutorial/) for that specifically.

If you want to mix action game elements into your narrative and produce native games for people to install on their own computers, you probably want to look into either [Ren'py](https://www.renpy.org/) and how it can be extended with Pygame if you're looking at mostly a visual novel with a few action elements *or* (if you're feeling brave, and mostly want to write an action game with narrative in it) you could start looking into the Unity game engine with Ink as a plugin. This second option is *much* more code and set up heavy though - it's the kind of thing where you may want to get yourself a good course or book to get you going.
