using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using System.Data.SQLite;

namespace TeBot
{    
    public class SQLManager
    {
        private SQLiteCommand sqlite_cmd;
        private SQLiteConnection sqlite;
        private SQLiteDataReader sqlite_datareader;

        public SQLManager(SQLiteConnection sqlite)
        {
            this.sqlite = sqlite;
        }

        public ulong CheckMatch(ulong id)
        {
            // Check DB to see if the entry exists, return an ID if it does, else return 0
            sqlite_cmd = sqlite.CreateCommand();
            sqlite_cmd.CommandText = "SELECT LinkID FROM SourceLinkIDPairs WHERE SourceID = " + id + ";";
            sqlite_datareader = sqlite_cmd.ExecuteReader();
            ulong readLinkId = 0;
            if (sqlite_datareader.Read())
            {
                readLinkId = (ulong)sqlite_datareader.GetInt64(0);
            }

            return readLinkId;
        }

        public void DeleteFromTable(ulong id)
        {
            // Delete entry from table
            sqlite_cmd = sqlite.CreateCommand();
            sqlite_cmd.CommandText = "DELETE FROM SourceLinkIDPairs WHERE SourceID = " + id + ";";
            sqlite_cmd.ExecuteNonQuery();
        }

        public void InsertToTable(ulong sourceID, ulong linkID)
        {
            sqlite_cmd = sqlite.CreateCommand();
            sqlite_cmd.CommandText = "INSERT INTO SourceLinkIDPairs (SourceID, LinkID) VALUES (" + sourceID + ", " + linkID + ");";
            sqlite_cmd.ExecuteNonQuery();
        }
    }
}
