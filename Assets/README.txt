= Choice of Games Save Manager for Steam (Unofficial) =

If you encounter any issues or have any suggestions, please don't hesitate
to contact me via one of these mediums:

- E-mail: yasirkula@gmail.com
- Forum: https://forum.choiceofgames.com/t/save-manager-and-editor-for-steam-open-source/107649/
- Reddit: https://www.reddit.com/user/yasirkula/
- GitHub: https://github.com/yasirkula/UnityChoiceOfGamesSaveManager/issues

This save manager is a hobby project of mine and I'm working on it alone,
so there may be some bugs that slip my attention. When you encounter such
bugs and report them to me, I'll try to fix them ASAP. While reporting bugs,
please include the contents of "CoG Save Manager_Data/output_log.txt" with
your bug report. Any errors encountered by the save manager are logged to
that file and these error messages can help me debug the issues much faster.

Save manager converts the games' save file names to their readable names
(e.g. 'keeperotsam' becomes 'Keeper of the Sun and Moon') by reading the
following database: https://gist.github.com/yasirkula/27302fcc36117b4741ea4817c1569434
To help expand this database with other games (which is very much appreciated),
you can either leave a comment on that page, or if you don't have a GitHub
account, you can contact me and I'll update the database for you. In either
case, you'll need the following information:

- GameID: this is the numeric value that is displayed at the end of 'Game Save Directory'
- SaveFileName: this is the value displayed in the title bar which we want to convert (e.g. "keeperotsam")
- ReadableName: the actual name of the game (e.g. "Keeper of the Sun and Moon")
