using System;
using System.IO;
using System.Text.Json;

namespace StressBotBenchmark
{
    public static class ScriptManager
    {
        private static readonly string ScriptsDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "scripts");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Garante que a pasta scripts/ existe.</summary>
        public static void EnsureDirectory()
        {
            if (!Directory.Exists(ScriptsDir))
                Directory.CreateDirectory(ScriptsDir);
        }

        /// <summary>Salva a config como script JSON.</summary>
        public static string Save(BotConfig config, string name)
        {
            EnsureDirectory();
            string safeName = SanitizeFileName(name);
            string path = Path.Combine(ScriptsDir, safeName + ".json");
            string json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>Carrega um script JSON e retorna BotConfig.</summary>
        public static BotConfig Load(string name)
        {
            EnsureDirectory();
            string safeName = SanitizeFileName(name);
            string path = Path.Combine(ScriptsDir, safeName + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Script '{name}' não encontrado em: {path}");
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BotConfig>(json, JsonOpts)
                   ?? throw new InvalidDataException("Script inválido");
        }

        /// <summary>Lista todos os scripts disponíveis.</summary>
        public static string[] ListScripts()
        {
            EnsureDirectory();
            var files = Directory.GetFiles(ScriptsDir, "*.json");
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        /// <summary>Deleta um script.</summary>
        public static bool Delete(string name)
        {
            string safeName = SanitizeFileName(name);
            string path = Path.Combine(ScriptsDir, safeName + ".json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
