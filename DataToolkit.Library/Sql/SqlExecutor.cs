using Dapper;
using DataToolkit.Library.Common;
using System.Data;
using System.Reflection;

namespace DataToolkit.Library.Sql;

/// <summary>
/// Ejecuta consultas SQL y procedimientos almacenados usando Dapper, 
/// proporcionando soporte para mapeo simple, múltiples resultados, interpolación segura
/// y parámetros de salida en procedimientos almacenados.
/// </summary>
public class SqlExecutor : IDisposable
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction? _transaction;
    private bool _disposed;

    /// <summary>
    /// Constructor principal del ejecutor SQL.
    /// </summary>
    /// <param name="connection">Conexión a la base de datos</param>
    /// <param name="transaction">Transacción activa (opcional)</param>
    public SqlExecutor(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    // ---------------------------------------------------------------------------
    // CONSULTAS INTERPOLADAS (seguros frente a inyección SQL)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Ejecuta una consulta SQL interpolada y devuelve una colección de resultados tipados.
    /// </summary>
    public IEnumerable<T> FromSqlInterpolated<T>(FormattableString query)
    {
        var (sql, parameters) = BuildInterpolatedSql(query);
        return _connection.Query<T>(sql, parameters, _transaction);
    }

    /// <summary>
    /// Ejecuta una consulta SQL interpolada de forma asíncrona.
    /// </summary>
    public async Task<IEnumerable<T>> FromSqlInterpolatedAsync<T>(FormattableString query)
    {
        var (sql, parameters) = BuildInterpolatedSql(query);
        return await _connection.QueryAsync<T>(sql, parameters, _transaction);
    }

    // ---------------------------------------------------------------------------
    // CONSULTAS CON MAPEOS MÚLTIPLES (JOIN de múltiples entidades)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Ejecuta una consulta con múltiples mapeos entre tablas relacionadas (JOIN).
    /// </summary>
    public IEnumerable<T> FromSqlMultiMap<T>(MultiMapRequest request)
    {
        var result = SqlMapper.Query(
            _connection,
            request.Sql,
            request.Types,
            objects => request.MapFunction(objects),
            param: request.Parameters,
            splitOn: request.SplitOn,
            transaction: _transaction,
            commandType: CommandType.Text
        );

        return result.Cast<T>();
    }

    /// <summary>
    /// Ejecuta una consulta con múltiples mapeos de forma asíncrona.
    /// </summary>
    public async Task<IEnumerable<T>> FromSqlMultiMapAsync<T>(MultiMapRequest request)
    {
        var result = await SqlMapper.QueryAsync(
            _connection,
            request.Sql,
            request.Types,
            objects => request.MapFunction(objects),
            param: request.Parameters,
            splitOn: request.SplitOn,
            transaction: _transaction,
            commandType: CommandType.Text
        );

        return result.Cast<T>();
    }

    // ---------------------------------------------------------------------------
    // CONSULTAS MULTI-RESULTADO (varios SELECT dentro de un SP)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Ejecuta un procedimiento almacenado o query que retorna múltiples conjuntos de resultados.
    /// </summary>
    public async Task<List<IEnumerable<dynamic>>> QueryMultipleAsync(
        string sql,
        object? parameters = null,
        CommandType commandType = CommandType.StoredProcedure)
    {
        var resultSets = new List<IEnumerable<dynamic>>();

        using var reader = await _connection.QueryMultipleAsync(sql, parameters, _transaction, commandType: commandType);

        while (!reader.IsConsumed)
        {
            var result = await reader.ReadAsync();
            resultSets.Add(result);
        }

        return resultSets;
    }

    // ---------------------------------------------------------------------------
    // EJECUCIÓN DE SQL (INSERT, UPDATE, DELETE)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Ejecuta una instrucción SQL (INSERT/UPDATE/DELETE).
    /// </summary>
    public int Execute(string sql, object? parameters = null)
    {
        return _connection.Execute(sql, parameters, _transaction);
    }

    /// <summary>
    /// Ejecuta una instrucción SQL de forma asíncrona.
    /// </summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        return await _connection.ExecuteAsync(sql, parameters, _transaction);
    }

    // ---------------------------------------------------------------------------
    // EJECUCIÓN CON PARÁMETROS DE SALIDA (OUTPUT)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Ejecuta un procedimiento almacenado que contiene parámetros OUTPUT (modo síncrono).
    /// </summary>
    public (int RowsAffected, Dictionary<string, object> OutputValues) ExecuteWithOutput(
        string storedProcedure,
        Action<DynamicParameters> configureParameters)
    {
        var parameters = new DynamicParameters();
        configureParameters(parameters);

        var rowsAffected = _connection.Execute(
            storedProcedure,
            parameters,
            _transaction,
            commandType: CommandType.StoredProcedure
        );

        var outputValues = new Dictionary<string, object>();
        foreach (var paramName in parameters.ParameterNames)
        {
            var value = parameters.Get<object>(paramName);
            outputValues[paramName] = value;
        }

        return (rowsAffected, outputValues);
    }

    /// <summary>
    /// Ejecuta un procedimiento almacenado que contiene parámetros OUTPUT (modo asíncrono).
    /// </summary>
    public async Task<(int RowsAffected, DynamicParameters Output)> ExecuteWithOutputAsync(
        string storedProcedure,
        Action<DynamicParameters> configureParameters)
    {
        var parameters = new DynamicParameters();
        configureParameters(parameters);

        var rows = await _connection.ExecuteAsync(storedProcedure, parameters, _transaction, commandType: CommandType.StoredProcedure);
        return (rows, parameters); // ← devolvemos los parámetros directamente
    }

    // ---------------------------------------------------------------------------
    // MÉTODO AUXILIAR PARA INTERPOLACIÓN SEGURA
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Convierte una cadena interpolada en SQL con parámetros seguros para Dapper.
    /// </summary>
    private static (string, DynamicParameters) BuildInterpolatedSql(FormattableString query)
    {
        var dParams = new DynamicParameters();
        var sql = query.Format;

        for (int i = 0; i < query.ArgumentCount; i++)
        {
            var paramName = $"@p{i}";
            sql = sql.Replace("{" + i + "}", paramName);
            dParams.Add(paramName, query.GetArgument(i));
        }

        return (sql, dParams);
    }

    // ---------------------------------------------------------------------------
    // IMPLEMENTACIÓN IDisposable
    // ---------------------------------------------------------------------------
    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Dispose();
            _disposed = true;
        }
    }
}
