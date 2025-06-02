using System;
using System.Threading.Tasks;
using Npgsql;

class Program
{
    static string connString = "Host=localhost; Port=5432; Username=postgres; Password=1111; Database=postgres";

    static async Task Main()
    {
        Console.Write("Enter username: ");
        var name = Console.ReadLine();

        Console.Write("Enter password: ");
        var password = Console.ReadLine();

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        int userId;

        // Check if user exists
        await using (var checkCmd = new NpgsqlCommand(@"SELECT ""Id"", ""Password"" FROM ""User"" WHERE ""Name"" = @Name", conn))
        {
            checkCmd.Parameters.AddWithValue("Name", name);
            await using var reader = await checkCmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                userId = reader.GetInt32(0);
                var storedPassword = reader.GetString(1);
                if (storedPassword != password)
                {
                    Console.WriteLine("❌ Incorrect password.");
                    return;
                }

                Console.WriteLine("✅ Login successful.");
            }
            else
            {
                await reader.CloseAsync();

                // Create new user
                await using var insertUser = new NpgsqlCommand(@"INSERT INTO ""User"" (""Name"", ""Password"") VALUES (@Name, @Password) RETURNING ""Id""", conn);
                insertUser.Parameters.AddWithValue("Name", name);
                insertUser.Parameters.AddWithValue("Password", password);
                userId = (int)await insertUser.ExecuteScalarAsync();
                Console.WriteLine("✅ User created and logged in.");
            }
        }

        // Prompt for score
        Console.Write("Enter your score: ");
        var scoreInput = Console.ReadLine();
        if (!int.TryParse(scoreInput, out int score))
        {
            Console.WriteLine("❌ Invalid score.");
            return;
        }

        // Upsert into leaderboard
        await using (var upsert = new NpgsqlCommand(@"
            INSERT INTO ""Leaderboard"" (""UserId"", ""Score"", ""Rank"")
            VALUES (@UserId, @Score, 0)
            ON CONFLICT (""UserId"") DO UPDATE SET ""Score"" = @Score;", conn))
        {
            upsert.Parameters.AddWithValue("UserId", userId);
            upsert.Parameters.AddWithValue("Score", score);
            await upsert.ExecuteNonQueryAsync();
        }

        // Recalculate ranks
        await using (var recalc = new NpgsqlCommand(@"
            WITH Ranked AS (
                SELECT ""UserId"", RANK() OVER (ORDER BY ""Score"" DESC) AS new_rank
                FROM ""Leaderboard""
            )
            UPDATE ""Leaderboard""
            SET ""Rank"" = Ranked.new_rank
            FROM Ranked
            WHERE ""Leaderboard"".""UserId"" = Ranked.""UserId"";", conn))
        {
            await recalc.ExecuteNonQueryAsync();
        }

        // Display top 10 leaderboard
        Console.WriteLine("\n🏆 Top 10 Leaderboard:");
        await using (var topCmd = new NpgsqlCommand(@"
            SELECT u.""Name"", l.""Score"", l.""Rank""
            FROM ""Leaderboard"" l
            JOIN ""User"" u ON l.""UserId"" = u.""Id""
            ORDER BY l.""Rank""
            LIMIT 10;", conn))
        {
            await using var topReader = await topCmd.ExecuteReaderAsync();
            Console.WriteLine("Rank | Name           | Score");
            Console.WriteLine("-------------------------------");

            while (await topReader.ReadAsync())
            {
                int rank = topReader.GetInt32(2);
                string uname = topReader.GetString(0);
                int uscore = topReader.GetInt32(1);
                Console.WriteLine($"{rank,4} | {uname,-14} | {uscore}");
            }
        }

        // Show current user's rank and score
        await using (var userInfoCmd = new NpgsqlCommand(@"
            SELECT ""Score"", ""Rank"" FROM ""Leaderboard"" WHERE ""UserId"" = @UserId", conn))
        {
            userInfoCmd.Parameters.AddWithValue("UserId", userId);
            await using var userReader = await userInfoCmd.ExecuteReaderAsync();
            if (await userReader.ReadAsync())
            {
                int yourScore = userReader.GetInt32(0);
                int yourRank = userReader.GetInt32(1);
                Console.WriteLine($"\n🙋 Your Score: {yourScore}, Rank: {yourRank}");
            }
        }
    }
}
