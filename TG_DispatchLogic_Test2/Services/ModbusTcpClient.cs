using System.Net.Sockets;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>Modbus TCP FC03 讀取 Holding Register（無額外 NuGet）。</summary>
public static class ModbusTcpClient
{
    public static async Task<ushort[]> ReadHoldingRegistersAsync(
        string host,
        int port,
        byte unitId,
        ushort startAddress,
        ushort count,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        if (count == 0) return [];

        using var tcp = new TcpClient();
        tcp.SendTimeout = timeoutMs;
        tcp.ReceiveTimeout = timeoutMs;
        await tcp.ConnectAsync(host, port, cancellationToken);

        using var stream = tcp.GetStream();
        stream.ReadTimeout = timeoutMs;
        stream.WriteTimeout = timeoutMs;

        var merged = new List<ushort>();
        ushort offset = 0;
        const ushort maxPerRequest = 125;

        while (offset < count)
        {
            var batch = (ushort)Math.Min(maxPerRequest, count - offset);
            var chunk = await ReadHoldingBatchAsync(
                stream, unitId, (ushort)(startAddress + offset), batch, cancellationToken);
            merged.AddRange(chunk);
            offset += batch;
        }

        return merged.ToArray();
    }

    /// <summary>單一 TCP 連線依序讀取多個位址區塊（Modbus 伺服器通常只允許一個連線）。</summary>
    public static async Task<Dictionary<int, int>> ReadHoldingRegisterBlocksAsync(
        string host,
        int port,
        byte unitId,
        IReadOnlyList<(ushort StartAddress, ushort Count)> blocks,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<int, int>();
        if (blocks.Count == 0) return values;

        using var tcp = new TcpClient();
        tcp.SendTimeout = timeoutMs;
        tcp.ReceiveTimeout = timeoutMs;
        await tcp.ConnectAsync(host, port, cancellationToken);

        using var stream = tcp.GetStream();
        stream.ReadTimeout = timeoutMs;
        stream.WriteTimeout = timeoutMs;

        foreach (var (startAddress, count) in blocks)
        {
            if (count == 0) continue;

            ushort offset = 0;
            const ushort maxPerRequest = 125;
            while (offset < count)
            {
                var batch = (ushort)Math.Min(maxPerRequest, count - offset);
                var chunk = await ReadHoldingBatchAsync(
                    stream, unitId, (ushort)(startAddress + offset), batch, cancellationToken);
                for (var i = 0; i < chunk.Length; i++)
                    values[startAddress + offset + i] = chunk[i];
                offset += batch;
            }
        }

        return values;
    }

    static int _txId;

    static async Task<ushort[]> ReadHoldingBatchAsync(
        NetworkStream stream,
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken)
    {
        var txId = (ushort)(Interlocked.Increment(ref _txId) & 0xFFFF);
        var req = new byte[12];
        req[0] = (byte)(txId >> 8);
        req[1] = (byte)(txId & 0xFF);
        req[2] = 0;
        req[3] = 0;
        req[4] = 0;
        req[5] = 6;
        req[6] = unitId;
        req[7] = 0x03;
        req[8] = (byte)(startAddress >> 8);
        req[9] = (byte)(startAddress & 0xFF);
        req[10] = (byte)(count >> 8);
        req[11] = (byte)(count & 0xFF);

        await stream.WriteAsync(req, cancellationToken);

        var header = new byte[9];
        await stream.ReadExactlyAsync(header, cancellationToken);

        if (header[7] == 0x83)
            throw new InvalidOperationException($"Modbus exception at address {startAddress}");

        var byteCount = header[8];
        var data = new byte[byteCount];
        await stream.ReadExactlyAsync(data, cancellationToken);

        var result = new ushort[byteCount / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);

        return result;
    }
}
