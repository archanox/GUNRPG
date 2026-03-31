namespace GUNRPG.Security;

/// <summary>
/// Persists and loads authority-signed <see cref="MerkleCheckpoint"/> artifacts for a run.
/// </summary>
/// <remarks>
/// <para>
/// Checkpoints are stored under <c>checkpoints/&lt;runId&gt;/&lt;tick&gt;.checkpoint</c>
/// relative to the configured base directory.  Each file contains the JSON serialisation
/// of a single <see cref="MerkleCheckpoint"/> (base-64 encoded byte arrays).
/// </para>
/// <para>
/// Only authority nodes should call <see cref="Save"/>; verifier nodes consume checkpoints
/// by calling <see cref="Load"/> or <see cref="TryLoadAll"/>.
/// </para>
/// </remarks>
public sealed class MerkleCheckpointStore
{
    private readonly string _baseDirectory;

    /// <summary>
    /// Initialises a new store that writes to / reads from <paramref name="baseDirectory"/>.
    /// The directory does not need to exist until the first <see cref="Save"/> call.
    /// </summary>
    /// <param name="baseDirectory">
    /// The root directory under which run checkpoint directories are created.
    /// Pass <c>"checkpoints"</c> for the conventional relative path.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="baseDirectory"/> is <see langword="null"/>.
    /// </exception>
    public MerkleCheckpointStore(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        _baseDirectory = baseDirectory;
    }

    /// <summary>
    /// Persists <paramref name="checkpoint"/> to
    /// <c>&lt;baseDirectory&gt;/&lt;runId&gt;/&lt;tick&gt;.checkpoint</c>.
    /// Creates parent directories as needed.
    /// Overwrites any existing file at that path.
    /// </summary>
    /// <param name="runId">The unique identifier of the run this checkpoint belongs to.</param>
    /// <param name="checkpoint">The checkpoint to persist.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="checkpoint"/> is <see langword="null"/>.
    /// </exception>
    public void Save(Guid runId, MerkleCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var path = GetPath(runId, checkpoint.Tick);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, checkpoint.ToJsonBytes());
    }

    /// <summary>
    /// Loads the checkpoint for <paramref name="tick"/> from
    /// <c>&lt;baseDirectory&gt;/&lt;runId&gt;/&lt;tick&gt;.checkpoint</c>.
    /// </summary>
    /// <param name="runId">The unique identifier of the run.</param>
    /// <param name="tick">The tick whose checkpoint to load.</param>
    /// <returns>The deserialised <see cref="MerkleCheckpoint"/>.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when no checkpoint file exists for the given run and tick.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the file content is not a valid serialised checkpoint.
    /// </exception>
    public MerkleCheckpoint Load(Guid runId, ulong tick)
    {
        var path = GetPath(runId, tick);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No checkpoint found for run {runId} at tick {tick}.", path);

        return MerkleCheckpoint.FromJsonBytes(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Attempts to load the checkpoint for <paramref name="tick"/>.
    /// Returns <see langword="null"/> when the file does not exist or cannot be parsed.
    /// </summary>
    /// <param name="runId">The unique identifier of the run.</param>
    /// <param name="tick">The tick whose checkpoint to load.</param>
    /// <returns>
    /// The deserialised <see cref="MerkleCheckpoint"/>, or <see langword="null"/> on failure.
    /// </returns>
    public MerkleCheckpoint? TryLoad(Guid runId, ulong tick)
    {
        try
        {
            return Load(runId, tick);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads all checkpoints for <paramref name="runId"/>, sorted by
    /// <see cref="MerkleCheckpoint.Tick"/> in ascending order.
    /// Returns an empty list when the run directory does not exist or contains no checkpoint files.
    /// Malformed checkpoint files are silently skipped.
    /// </summary>
    /// <param name="runId">The unique identifier of the run.</param>
    public IReadOnlyList<MerkleCheckpoint> TryLoadAll(Guid runId)
    {
        var dir = GetRunDirectory(runId);
        if (!Directory.Exists(dir))
            return [];

        var results = new List<MerkleCheckpoint>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.checkpoint"))
        {
            try
            {
                results.Add(MerkleCheckpoint.FromJsonBytes(File.ReadAllBytes(file)));
            }
            catch (System.Text.Json.JsonException)
            {
                // Silently skip malformed JSON checkpoint files.
            }
            catch (FormatException)
            {
                // Silently skip checkpoint files with invalid data formats.
            }
        }

        results.Sort(static (a, b) => a.Tick.CompareTo(b.Tick));
        return results;
    }

    /// <summary>
    /// Returns <see langword="true"/> if a checkpoint file exists for the given run and tick.
    /// </summary>
    public bool Exists(Guid runId, ulong tick) => File.Exists(GetPath(runId, tick));

    private string GetRunDirectory(Guid runId) =>
        Path.Combine(_baseDirectory, runId.ToString("N"));

    private string GetPath(Guid runId, ulong tick) =>
        Path.Combine(GetRunDirectory(runId), $"{tick}.checkpoint");
}
