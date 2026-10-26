# Ambrosia
This mod adds a new Liquid that can transform items into other items.  
This mod needs to be on both the Client and the Server to work properly.  
This mod requires that you have https://github.com/Nyx12012/CookingLib downloaded to work properly.  

You can obtain Ambrosia in two ways, either by going to the infinite crate of it at the top of the mountain in Mountain Pass, or by placing a MRK Fuel Core into water.  
You can drink Ambrosia to gain 8 bars of healing, or place an item in it to possibly transform that item, assuming it has a recipe when put into Ambrosia.  

As a note for mod makers, Ambrosia is added to the DyeingAPI global liquid blacklist.  
As a note for server owners, this mod currently uses MelonPreferences to store whether or not the crate at the top of the mountain has been spawned. This method, while it does prevent the crate from spawning on every reboot, it has the issue of not being reset on any form of wipe, so you need to manually change the toggle back to true to respawn it on the next reboot, or spawn a crate in yourself. I intend to change how it works in order to make it spawn again upon a wipe of the server/chunk.  

This is an unofficial, fan-made mod for A Township Tale. This mod is not affiliated with or endorsed by the owners or developers of A Township Tale.  
