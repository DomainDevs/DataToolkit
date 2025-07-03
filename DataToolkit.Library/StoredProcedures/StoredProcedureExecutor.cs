using AdoNetCore.AseClient;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;

namespace DataToolkit.Library.StoredProcedures;

public class StoredProcedureExecutor : IStoredProcedureExecutor
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction? _transaction;

    public StoredProcedureExecutor(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public DataSet ExecuteDataSet(string procedure, IEnumerable<IDbDataParameter> parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = procedure;
        cmd.CommandType = CommandType.StoredProcedure;

        if (_transaction != null)
            cmd.Transaction = _transaction;

        if (parameters != null)
        {
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
        }

        var ds = new DataSet();
        var adapter = CreateAdapter(cmd);
        adapter.Fill(ds);
        return ds;
    }

    public async Task<DataSet> ExecuteDataSetAsync(string procedure, IEnumerable<IDbDataParameter> parameters)
    {
        // ADO.NET does not support async fill — wrap in Task.Run
        return await Task.Run(() => ExecuteDataSet(procedure, parameters));
    }

    public DataTable ExecuteDataTable(string procedure, IEnumerable<IDbDataParameter> parameters)
    {
        return ExecuteDataSet(procedure, parameters).Tables[0];
    }

    public async Task<DataTable> ExecuteDataTableAsync(string procedure, IEnumerable<IDbDataParameter> parameters)
    {
        var ds = await ExecuteDataSetAsync(procedure, parameters);
        return ds.Tables[0];
    }

    private static DbDataAdapter CreateAdapter(IDbCommand cmd)
    {
        return cmd switch
        {
            SqlCommand sql => new SqlDataAdapter(sql),
            AseCommand ase => new AseDataAdapter(ase),
            _ => throw new NotSupportedException("Comando no soportado.")
        };
    }
}