using System.Data;
using System.Globalization;
using GeoPulse.Core.Repositories;
using Npgsql;

namespace GeoPulse.Infrastructure.WellIds
{
    /// <summary>
    /// Резолв npdWellBoreCode → well_id по PostgreSQL с кэшем положительных результатов.
    /// Первое обращение загружает весь справочник одним запросом, промах кэша уточняется
    /// точечным запросом (скважина могла быть зарегистрирована позже), отсутствующие коды
    /// не кэшируются — иначе событие, пришедшее раньше well.registered, «залипло» бы в DLQ навсегда.
    /// </summary>
    public sealed class WellIdCache : IWellIdResolver, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<int, int> _wellIds = new();
        private NpgsqlConnection? _connection;
        private bool _loaded;

        /// <summary>Конструктор.</summary>
        /// <param name="connectionString">Строка подключения к PostgreSQL.</param>
        public WellIdCache(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <inheritdoc />
        public async Task<int?> ResolveAsync(int npdWellBoreCode, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var connection = await GetConnectionAsync(cancellationToken);

                if (!_loaded)
                {
                    await LoadAllAsync(connection, cancellationToken);
                    _loaded = true;
                }

                if (_wellIds.TryGetValue(npdWellBoreCode, out var cached))
                {
                    return cached;
                }

                var resolved = await QuerySingleAsync(connection, npdWellBoreCode, cancellationToken);
                if (resolved is null)
                {
                    return null;
                }

                _wellIds[npdWellBoreCode] = resolved.Value;
                return resolved;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _gate.Dispose();
        }

        private async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
        {
            if (_connection is { State: ConnectionState.Open })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);
            return _connection;
        }

        private async Task LoadAllAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT npd_well_bore_code, id FROM wells", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                _wellIds[reader.GetInt32(0)] = reader.GetInt32(1);
            }
        }

        private static async Task<int?> QuerySingleAsync(
            NpgsqlConnection connection,
            int npdWellBoreCode,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                "SELECT id FROM wells WHERE npd_well_bore_code = @code", connection);
            command.Parameters.AddWithValue("@code", npdWellBoreCode);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
    }
}
