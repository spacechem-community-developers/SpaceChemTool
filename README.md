# SpaceChemTool (SCT for friends)

SpaceChemTool is a tool for import and exporting SpaceChem puzzle solutions. It aims to:

* Minimize the time spent reproducing solutions
* Provide solvers and results video with graphical aids for additional restrictions (atom barriers, no waldo zones etc.)
* Provide solvers with a complete set of solutions for each completed round

The tool was used for all the SpaceChem Tournaments after 2015.  

### Typical usage for round 1
1. Download the tool using the `Clone or download` -> `Download ZIP` button over there
2. Open config.txt and replace Solver with your name in the USER=... line
3. Open command prompt (cmd.exe) and go to the tool directory
4. Run `build_windows.bat` (or `build_nix.sh`) to generate the .exe
5. Get the first round from the host and copy it into the tool directory
6. Run `SpaceChemTool play round1`
7. Solve the puzzles
8. Run `SpaceChemTool export round1` or `SpaceChemTool export <puzzle_name>` and send the exportedXXX.txt file generated
   (see output from the tool for the filename) to the host
9. Wait for results to be published
10. Run `SpaceChemTool import round1`
11. Look at everyone's solutions in game


### Typical usage for other rounds
* Steps 5-11 as above with new round and puzzle names

## Puzzle directories
Each puzzle directory (`round1..N`) is provided by the host and contains material for a round of the tournament:
* `*.puzzle` are the puzzles
* `*.images` are reactor image patches for puzzles that have restrictions e.g.
    * barriers that atoms cannot pass through
    * cells that waldos cannot enter
* `solutions.txt` contains exported solutions - this will be overwritten when multiple exports of a round are performed. This is likely to be changed in a later version of the tool.

## Content of the tool directory
* `build_windows.bat` - batch file to build SpaceChemTool.exe on Windows
* `build_nix.sh` - sh script to build SpaceChemTool.exe on MacOSX/Linux
* `SpaceChemTool.exe` - the tool for importing/exporting puzzles and solutions (present after compilation)
* `config.txt` (`default_config.txt` before first use) - a user specific configuration file that needs to be setup once. 
* `SpaceChemTool.cs` - the source for SpaceChemTool for anyone interested or wishing to help develop it
* `System.Data.SQLite.dll` - referenced by SpaceChemTool.exe for access to SQLite files
* `sqlite3.dll` - Used by System.Data.SQLite.dll on Windows (Mac/Linux users should already have a version of this installed)
* `*.tex` - images for reactor features

## Config file (`config.txt`)
* `USER` - The solver's tournament username which should match their steam user name
* `PLAYSAVE` - The name of the "Play" user and save file which the solver will use to solve the tournament puzzles.
               This must not be the name of an existing SpaceChem profile.
* `IMPORTSAVE` - The name of the "Import" user and save file which solver and observers can use to view all solutions to completed puzzles.
                 This must not be the name of an existing SpaceChem profile but can be the same as PLAYSAVE although it isn't the recommended usage.
* `IMAGEPATH` (optional) - The path for the Spacechem images directory (contains files `000.tex`-`054.tex` + a few other files).
                           Steam installs should be automatically detected on all platforms.

The tool SpaceChemTool needs to be run from a command prompt with this directory as the current directory.  
Using the tool while the game is running is not recommended.  
Running the tool on OSX/Unix requires mono (prefix the following commands with mono).

## SCT commands in detail
```
SpaceChemTool.exe play [ROUND NAME]
	This imports the puzzles for [ROUND NAME] into the "Play" save file and informs the solver which puzzles have reactor images available. If the "Play" user hasn't been added then it will be automatically added.

SpaceChemTool.exe images [PUZZLE NAME]
	This patches the reactor images for [PUZZLE NAME] or removes patches if no puzzle is specified.

SpaceChemTool.exe export [ROUND OR PUZZLE NAME] [OPTIONAL FILTER]
	This exports all solutions for [PUZZLE NAME] or puzzles in [ROUND NAME] from the "Play" save file into an export*.txt file (see output for name of exported file)

SpaceChemTool.exe import [ROUND NAME]
	This imports all solutions for puzzles in [ROUND NAME] to the "Import" save file from solutions.txt. If the "Import" user hasn't been added then it will be automatically added.

SpaceChemTool.exe copy [PUZZLE NAME] [OPTIONAL FILTER]
SpaceChemTool.exe copyswapped [PUZZLE NAME] [OPTIONAL FILTER]
	This copies the solution for [PUZZLE NAME] matching the filter in the "Play" save file. If no solutions or more than one solution is found, nothing is copied. Copyswapped swaps the red and blue waldos in the process.

SpaceChemTool.exe stats [PUZZLE NAME] [OPTIONAL FILTER]
	This displays extended stats for all solutions for [PUZZLE NAME] matching the filter in the "Play" save file.

SpaceChemTool.exe addusers
	This adds users for the tournament which temporarily replace 2 of the 3 user profiles in SpaceChem.

SpaceChemTool.exe removeusers
	This removes users for the tournament and restores the user profiles in SpaceChem. This doesn't remove the save files so calling addusers again will allow solvers to pick up where they left off. 
```
