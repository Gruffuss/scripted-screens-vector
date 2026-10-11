-- 19 - Fonts from the web: a page that brings its own typeface
--
-- `font=` on a `T` names a face that is already registered: the game's own, or one a player
-- dropped into the ScriptedScreens Fonts mod's folder. A page can also ASK for one. A `FONT`
-- line inside `DEFS` names a Google Fonts link, every player's game fetches it, and each label
-- using it switches to the new face when it arrives. Nothing is pushed again.
--
--     DEFS { FONT src="https://fonts.googleapis.com/css2?family=Orbitron:wght@400;700" }
--
-- NEEDS THE SCRIPTEDSCREENS FONTS MOD, which does the fetching; this mod never downloads anything
-- itself. The fonts mod fetches only from the hosts its settings allow, Google Fonts by default.
--
-- THE NAMES COME FROM THE FONT, NOT FROM THE LINK. A face is called "Family" for its regular
-- weight and "Family Style" otherwise, from the file's own metadata. `wght@400;700` brings two
-- faces, "Orbitron" and "Orbitron Bold", and a weight the link did not ask for does not exist.
-- `vector_stats` lists what each link delivered under FONTS, and names any `font=` that did not
-- arrive, so the first push tells you the exact names to write.
--
-- WRITE THE PAGE TO READ WELL WITHOUT IT. Each player fetches the font for themselves, under
-- their own settings: a player without the fonts mod, with page fonts switched off, or offline
-- sees the game's own font. Size and lay out the labels so that still works.
--
-- INLINE, TOO. A `<font=Name>...</font>` tag inside a label's text picks a face for part of it.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local src = table.concat({
    "SCENE w=200 h=200 fit=stretch size=8 f=#DDE7EE",
    -- The link. One FONT line per link; a page may declare several. A link is fetched once per
    -- game session and shared by every page that declares the same one.
    "DEFS {",
    '  FONT src="https://fonts.googleapis.com/css2?family=Orbitron:wght@400;700"',
    "}",
    "R x=0 y=0 w=200 h=200 f=#0E1A26 fea=0",
    "R x=10 y=10 w=180 h=40 rx=4 f=#16283A s=#3E6A8C sw=1",
    -- The Bold weight, by the name the font itself gives it.
    'T x=18 y=15 w=164 h=16 font="Orbitron Bold" size=12 f=#F2C14E text="REACTOR 2"',
    'T x=18 y=33 w=164 h=12 font=Orbitron size=6 f=#8FB8D8 text="primary loop nominal"',
    -- Digits are where a display face earns its keep.
    'T x=10 y=62 w=180 h=34 font=Orbitron size=22 align=center text="1,482 kPa"',
    -- Inline: one span in another face, the rest in the label's own.
    'T x=10 y=112 w=180 h=14 size=7 align=center text="core at <font=Orbitron>612 K</font>, within limits"',
    -- This line names no font, so it reads the same for every player.
    'T x=10 y=170 w=180 h=20 size=5 wrap=1 align=center f=#6F8799 text="Without the fonts mod every line above still reads, in the game\'s own font."',
}, "\n")

ui:element({ id = "webfonts", type = "vector",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { scene = "webfonts", src = src } })
ui:commit()
