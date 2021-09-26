# RimLanguageHotReload (Horlivec)
Modification for RimWorld to help language translation producers with their work.

Automatically tries to hot reload any changes applied to active language files to show them immediately in-game.

The modification was initially developed for Czech translation but sure can be used for any other language.

## Usage

Subscribe on [Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=2569378701).

Run game and add Rim Language Hot Reload to active mods.

Enjoy editing files in language folders. 

The Horlivec attach to active language and probes for changes in files in active languge folders. Only physically existing folders are probed. Horlivec cannot be used for virtual ones (in .TAR file).

Not all texts are available to translate and not all translated texts are able to be changed while the game is running and some are cached and need to be invalidated which is not allways easy task. But the list of available scenarios will grow up with each update.

## Limitations

There are some known limitations which can be fixed but are of low priority.

- The Horlivec bypass modification and DLC inheritance. If some text is overriden in modification/DLC after change in Core (same file, same property is not needed) the translation is gotten from Core instead of modification/DLC.

- Changes made to strings files (in \Langugage\Strings\ folder) are reflected after change in rulesStrings/rulesFiles property for particular definition. 

## Caution

The Horlivec uses unsafe reflection API and can cause damage to game integrity leading to crash or corrupted game state or files.

Not all texts applied by Horlivec would be the same as if loaded natively. It tries to simulate game behavior but it is so complex. Be sure to view applied changes without mod before publishing.

## TODO
The Horlivec mainly completed. Future work will focus on increase performance and fix some bugs if found. There are currently no missing translations that I'm aware of.

- Rules strings files dows not seem to work properperly.
