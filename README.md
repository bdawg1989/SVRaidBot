# Sc/Vi RaidBot

## Hello, and welcome to my RaidBot Project.

![image](https://github.com/bdawg1989/NotRaidBot/assets/80122551/258bd7ab-0982-4641-9a3d-41bd192828dc)

![image](https://github.com/bdawg1989/NotPaldeaNET/assets/80122551/cc9e2d9e-eb54-4717-a896-b7afa163e1c3)

![image](https://github.com/bdawg1989/NotRaidBot/assets/80122551/a9aeb158-b6b6-415e-aa76-0e692d1283ce)

# How-To Video
https://youtu.be/wlFE04oiqGs?si=EDoVRaocGyCapN9n

# __Features__
## Auto Teleport 
- Teleports user to the nearest raid den if raid den is lost.
## Raid Requests
__Adding Requests__
 - Users can request their own raids using command `ra <seed> <difficulty> <storyprogress>`.
 - I have an online seed finder located here for your users to use: [https://genpkm.com/raids/seeds/](https://genpkm.com/raids/seeds/index.php)
   
__Removing Request__
- Users can remove thier raid request by simply typing the `rqc` command.  This removes them from the queue.
  
__RaidView__
- Users can view a raid embed with all the details of the raid including stats, rewards, and more!  Command `rv <seed> <difficulty> <storyprogress>`
  
__Disable Requests__
- The bot owner can enable or disable requesting raids at the click of a button.
  
__Limit Requests__
 - The bot owner can decide how many requests a user can submit in a certain time limit if they wish.
   
## Full Event Support
- Users can request an active event they want.  The bot will auto teleport your player to the nearest Event den and proceed with overwriting the seed.
- Auto Detects Might and Distribution Group ID's to be used. 
## Mystery Raids
- Don't have time to come up with your own raids to add to the list?  Turn on the Mystery Raids function and get a random shiny raid each time!
## Disable Overworld Spawns
- Tired of your player getting in random battles with overworld pokemon?  Turn this feature on and there will be no more overworld spawns to deal with!
## Embed Settings
- Tons of embed settings for the bot owner to choose from.  Customize it your way!
## Auto Story Progress 
- StoryProgress will automatically edit the game flags to change the story progress for you per raid!  No need for two bots anymore for "baby" raids and "adult" raids!  

# __SV Raid Bot Guide__

 - **ActiveRaids**
  - Changing Battle Pokémon - Inside of the collection editor is where all of your requested and auto rotate Pokémon live.  You can change the bots Pokémon by editing the `PartyPK` setting and opening up the editor.  Here, you will put in your desired showdown format for the Pokémon you wish the bot to use for the raid.  The bot will use that Pokémon for that raid only.  Once the raid is done, the original Pokémon that you had first in your party will be used in the next raid unless the next raid also has a PartyPK filled out.
  - New 4/27/25 - When starting the program for the first time, the bot will automatically add two random shiny raids to your ActiveRaids list.  That way, you don't have to figure out how to add raids and can start right away, especially if you turn the Mystery Raid feature on.  This makes the program a lot more beginner friendly.
- **RaidSettings**
 - GenerateRaidsFromFile - Set this to `True` and add your own seeds that you want rotated to this file.  When you start up the program for the first time, it will create a new folder called `raidfilessv`    for you and add the `raidsv.txt` file to it.  You will open this file, and add your seeds to it like this `<seed>-<speciesname>-<stars/difficulty>-<storyprogresslevel>`
   - Example:  If i'm looking at Raidcalc and your settings were  Story Progress: 4* Unlocked and Stars: 3, you would add that seed in as `3739A70B-Goomy-3-4`
   - As of 10/25/23 I include two templates for you in the folder - paldeaseeds.txt and kitakamiseeds.txt - you can copy those seeds and add to your raidsv.txt file as a starting point.
  - Save `raidsv.txt` with your new changes.
  - Start NotRaidBot and the list from raidsv.txt will now begin to populate the list inside of the setting `ActiveRaids`.  
 - SaveSeedsToFile - Set to true so that the bot saves a back up of your current ActiveRaids so you can paste them back to raidsv.txt if you ever need to.
 - RandomRotation - Set to true if you want the bot to do random raids in your ActiveRaids list while also keeping Requested raids a priority.
 - MysteryRaids - Set to true for the bot to randomly inject a shiny raid.  Cannot be used with RandomRotation on.
 - DisableRequests - Disables users from being able to request raids.
 - DisableOverworldSpawns - Stops wild pokemon from spawning in overworld if set to true.  Set back to false to make pokemon spawn.
 - KeepDaySeed - Set this to True so that the bot will inject the correct Today Seed if it rolls over to tomorrow.
 - EnableTimeRollback - Set to true for bot to roll time back 5 hours to prevent the day from changing.

- **Embed Toggles**
 - RaidEmbedDescription - add any text you want to show on *all* embeds posted at the top.  
 - SelectedTeraIconType - This changes the icons used in your embed.  Icon1 are custom tera icons that look amazing.
 - IncludeMoves - set to true if you want to show the moves the raid mon will have in the embed.
 - IncludeRewards - set to true if you want to show the rewards the raid mon will have in the embed.
 - IncludeSeed - Set to true to show the current raid seed in the embed.
 - IncludeCountdown - Set to true to show time until Raid starts in embed.
 - IncludeTypeAdvantage - Set to true to show super effective types in embed.
 - RewardsToShow - A list of rewards you want to show on your embeds.
 - RequestEmbedTime - Time to wait to post user requested raids to public channel.
 - TakeScreenShot - Set to true to show screenshot of the game in your embeds.
 - ScreenshotTiming - Set to 1500ms or 22000ms to take different screenshots once in raid.
 - HideRaidCode - Hides raid code from embed.

- **EventSettings**
 - EventActive - Set to true if Event is active (Might or Distribution).  The bot will auto set this if Event is detected.
 - RaidDeliveryGroupID - The Event Index of the current Event goes here.  The bot will auto set this if Event is detected. 

- **LobbyOptions**
 - LobbyMethod
  -  SkipRaid and it will just skip the raid if it's empty after the specified times defined in `SkipRaidLimit`
  - Continue - will just continue posting the same raid until someone joins.  
  - OpenLobby - will open the lobby as Free For All after X amount of Empty Lobbies.
 - Action - Set this to `MashA` so that the bot presses "A" in the game every 3.5s in battle.  `AFK` means the bot will not do anything.
 - ExtraTimeLobbyDisband - once lobby is disbanded, add extra time to return to overworld if needed.  
 - ExtraTimePartyPK - Extra time to wait to switch your lead raid mon to battle with.  Slow switches only.

- **RaiderBanList**
 - List - This is where all NID's of banned people go.  Use `ban <playername or NID>` to ban or add them manually here.
 - AllowIfEmpty - Keep false.

- **MiscSettings**
 - DateTimeFormat - Set the time format as it appears on your switch. 
 - UseOvershoot - If true, bot will use overshoot method instead of pressing DDown to get to date/time settings.  If true, be sure to ConfigureRolloverCorrection.
 - DDownClicks - Times bot needs to press DDown to get to Date/Time Settings.
 - ScreenOff - Turns your screen off while playing to preserve LED/Power.   Or use commands `screenOff` or `screenOn`.
- **DiscordSettings**
- Token - Add your discord bot token you got from the [Discord Developer Portal](<https://discord.com/developers/applications/>)
- CommandPrefix - the prefix your bot will use for commands.  Common is $
- RoleSudo - Tell the bot who it's daddy (or mommy) is.  Go to your server in a channel the bot has permission to read and type `$sudo @YOURUSERNAME`.  The bot is now under your command.
- ChannelWhitelist - these are channels that you want your bot to listen to commands in.  Use `$addchannel` to add a channel to the bot automatically.
- LoggingChannels - if you want to log all the stuff your bot puts in the Log Tab of the program but in a channel, use the `$loghere` command.
- EchoChannels - These are channels you want your raid embeds to post to.  Use command `$aec` to add the channel to this list.

## __Announcement Settings__

This is helpful if your bot is in several servers and you need to let everyone know that's using it that the bot is online, offline, napping, etc. without having to send out tons of messages yourself.  Just use the `$announce TEXT HERE`command to send out a nice announcement wrapped in a beautiful embed with your choice of thumbnail image and color.
- AnnouncementThumbnailOption - Set this to your fave pokemon image that i've premade.
- CustomAnnouncementThumbnailURL - Put the url to your own thumbnail image if you don't like mine.
- AnnouncementEmbedColor - Self explanatory.
- RandomAnnouncementThumbnail - set to true if you want it to use random images from my custom thumbnails.  Does not work if you have a custom image you're using.
- RandomAnnouncementColor - Let the bot choose from the list what color the embed will be this time.

# __In-Game Set Up__
- Stand in front of your raid crystal
- In game Options, make sure of the following:
 -  `Give Nicknames` is Off
 - `SendToBoxes` is `Automatic`(in case bot catches raid mon)
 - Auto Save is `Off`
 - Text Speed is `Fast`
- Start the bot

## __Program Setup__
- Enter raid description as you like or leave it blank
- To post raid embeds in a specific channel use the `aec` command.
- Paste your raid's seed in the Seed parameter.
- **Code the raid**
 - Set to true if you want a coded raid
- **TimeToWait**
 - Total time to wait before attempting to enter the raid.
- **DateTimeFormat**
 - Set the proper date/time format in your settings for when its time to apply rollover correction.
- **TimeToScrollDownForRollover**
 - For this you want to OVERSHOOT the Date/Time setting
 - ~800 is for Lites, ~950 for OLEDs, and ~920 for V1s
 - Time will vary for everyone.
- **ConfigureRolloverCorrection**
 - If true will only run the rollovercorrection routine for you to figure out your timing
 - Run this when the game is closed.


# All of my Projects

## Showdown Alternative Website
- https://genpkm.com - An online alternative to Showdown that has legality checks and batch trade codes built in to make genning pokemon a breeze.

## Scarlet/Violet RaidBot

- [NotRaidBot](https://github.com/bdawg1989/NotPaldeaNET) - The most advanced RaidBot for Scarlet/Violet available, period.
  
## PKHeX - AIO (All-In-One)

- [PKHeX-AIO](https://github.com/bdawg1989/PKHeX-ALL-IN-ONE) - A single .exe with ALM, TeraFinder, and PokeNamer plugins included.  No extra folders and plugin.dll's to keep up with.

## MergeBot - The Ultimate TradeBot

- [Source Code](https://github.com/bdawg1989/MergeBot)

## Grand Oak - SysBot Helper
- A discord bot that helps with legality issues if someone submits a wrong showdown format.  [Join My Discord To Learn More](https://discord.gg/GtUu9BmCzy)
  
![image](https://github.com/bdawg1989/MergeBot/assets/80122551/0842b48e-1b4d-4621-b321-89f478db508b)
