using System.Diagnostics;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public class ModbusEquipPollService
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<EquipSimLiveSnapshot> PollSnapshotAsync(
        ModbusEquipCatalog catalog,
        string host,
        int port,
        byte unitId,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        try
        {
            var values = await PollValuesAsync(catalog, host, port, unitId, timeoutMs, cancellationToken);
            return EquipSimSnapshotBuilder.Build(
                catalog, values, true, null, (int)sw.ElapsedMilliseconds, DateTime.Now);
        }
        catch (Exception ex)
        {
            return EquipSimSnapshotBuilder.Build(
                catalog, new Dictionary<int, int>(), false, ApiErrorFormatter.Modbus(ex.Message),
                (int)sw.ElapsedMilliseconds, DateTime.Now);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<Dictionary<int, int>> PollValuesAsync(
        ModbusEquipCatalog catalog,
        string host,
        int port,
        byte unitId,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        var blocks = catalog.PollBlocks
            .Select(b => ((ushort)b.StartAddress, (ushort)b.Count))
            .ToList();

        if (blocks.Count == 0)
            return new Dictionary<int, int>();

        return await ModbusTcpClient.ReadHoldingRegisterBlocksAsync(
            host, port, unitId, blocks, timeoutMs, cancellationToken);
    }
}
