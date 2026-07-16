namespace CanTools;

/// <summary>
/// Converts between the two bit numbering schemes: DBC's sawtooth numbering counts bits
/// from the LSB within each byte, network numbering from the MSB. The mapping swaps the
/// bit position within its byte and is therefore its own inverse.
/// </summary>
internal static class BitNumbering
{
    // Upstream: utils.sawtooth_to_network_bitnum.
    public static int SawtoothToNetwork(int bit) => 8 * (bit / 8) + (7 - bit % 8);
}
