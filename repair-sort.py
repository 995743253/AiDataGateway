import io

path = 'src/AiDataGateway.Infrastructure/Logs/NLogConfigurationResolver.cs'
text = io.open(path, encoding='utf-8').read()

# Folder branch: newest files first so the read budget reaches the latest logs
old = """        if (Directory.Exists(fullPattern))
        {
            return FilterFiles(EnumerateFiles(fullPattern, "*.log", recursive: true), fromUtc, toUtc)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(500)
                .ToArray();
        }"""
new = """        if (Directory.Exists(fullPattern))
        {
            // Newest first: the read budget is limited, so the latest logs must be parsed before older ones.
            return FilterFiles(EnumerateFiles(fullPattern, "*.log", recursive: true), fromUtc, toUtc)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToArray();
        }"""
assert old in text
text = text.replace(old, new, 1)
io.open(path, 'w', encoding='utf-8', newline='').write(text)
print('resolver folder branch updated')

# Wildcard branch: same newest-first ordering
path = 'src/AiDataGateway.Infrastructure/Logs/NLogConfigurationResolver.cs'
text = io.open(path, encoding='utf-8').read()
old = """        var pathMatcher = BuildPathMatcher(fullPattern);
        return FilterFiles(EnumerateFiles(directory, pattern, recursive: true).Where(path => pathMatcher.IsMatch(path)), fromUtc, toUtc)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(500)
            .ToArray();"""
new = """        var pathMatcher = BuildPathMatcher(fullPattern);
        return FilterFiles(EnumerateFiles(directory, pattern, recursive: true).Where(path => pathMatcher.IsMatch(path)), fromUtc, toUtc)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(500)
            .ToArray();"""
# keep as-is: already fine
io.open(path, 'w', encoding='utf-8', newline='').write(text)
print('wildcard branch checked')
PYEOF