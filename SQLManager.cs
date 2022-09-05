using System.Data.SQLite;

namespace TeBot
{
    public class SQLManager
    {
        private const string CROSSPOST_SELECT = "SELECT LinkID FROM SourceLinkIDPairs WHERE SourceID = ";
        private const string CROSSPOST_INSERT_0 = "INSERT INTO SourceLinkIDPairs (SourceID, LinkID) VALUES (", CROSSPOST_INSERT_1 = ",", CROSSPOST_INSERT_2 = ")";
        private const string CROSSPOST_DELETE = "DELETE FROM SourceLinkIDPairs WHERE SourceID = ";

        private readonly SQLiteConnection sqlite;

        public SQLManager(SQLiteConnection sqlite)
        {
            this.sqlite = sqlite;
        }

        public ulong CheckMatch(ulong id)
        {
            // Check DB to see if the entry exists, return an ID if it does, else return 0
            SQLiteCommand sqliteCommand = sqlite.CreateCommand();
            sqliteCommand.CommandText = CROSSPOST_SELECT + id;

            SQLiteDataReader sqliteReader = sqliteCommand.ExecuteReader();
            if (sqliteReader.Read())
            {
                return (ulong)sqliteReader.GetInt64(0);
            }

            return CommandHandler.NO_ID;
        }
        public void InsertToTable(ulong sourceID, ulong linkID)
        {
            SQLiteCommand sqliteCommand = sqlite.CreateCommand();
            sqliteCommand.CommandText = CROSSPOST_INSERT_0 + sourceID + CROSSPOST_INSERT_1 + linkID + CROSSPOST_INSERT_2;
            sqliteCommand.ExecuteNonQuery();
        }

        public void DeleteFromTable(ulong id)
        {
            // Delete entry from table
            SQLiteCommand sqliteCommand = sqlite.CreateCommand();
            sqliteCommand.CommandText = CROSSPOST_DELETE + id;
            sqliteCommand.ExecuteNonQuery();
        }
    }
}
