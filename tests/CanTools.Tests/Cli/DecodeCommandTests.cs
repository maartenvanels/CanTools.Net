namespace CanTools.Tests.Cli;

public class DecodeCommandTests
{
    private static void AssertDecode(string[] args, string input, string expected)
    {
        var (exitCode, stdout, stderr) = CliRunner.Run(args, input);

        Assert.Equal("", stderr);
        Assert.Equal(0, exitCode);
        Assert.Equal(expected, stdout);
    }

    // ported from test_command_line.py::test_decode
    [Fact]
    public void Decode_candump()
    {
        var input = """
              vcan0  0C8   [8]  F0 00 00 00 00 00 00 00
              vcan0  064   [10]  F0 01 FF FF FF FF FF FF FF FF
              vcan0  ERROR

              vcan0  1F4   [4]  01 02 03 04
              vcan0  1F3   [3]  01 02 03
            """;

        var expected = """
              vcan0  0C8   [8]  F0 00 00 00 00 00 00 00 ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
              vcan0  064   [10]  F0 01 FF FF FF FF FF FF FF FF :: Wrong data size: 10 instead of 1 bytes
              vcan0  ERROR

              vcan0  1F4   [4]  01 02 03 04 ::
            IO_DEBUG(
                IO_DEBUG_test_unsigned: 1,
                IO_DEBUG_test_enum: IO_DEBUG_test2_enum_two,
                IO_DEBUG_test_signed: 3,
                IO_DEBUG_test_float: 2.0
            )
              vcan0  1F3   [3]  01 02 03 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", TestFiles.Dbc("socialledge.dbc")], input, expected);
    }

    // ported from test_command_line.py::test_decode_timestamp_absolute
    [Fact]
    public void Decode_absolute_timestamps()
    {
        var input = """
             (2020-12-19 12:04:45.485261)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00
             (2020-12-19 12:04:48.597222)  vcan0  064   [8]  F0 01 FF FF FF FF FF FF
             (2020-12-19 12:04:56.805087)  vcan0  1F4   [4]  01 02 03 04
             (2020-12-19 12:04:59.085517)  vcan0  1F3   [3]  01 02 03
            """;

        var expected = """
             (2020-12-19 12:04:45.485261)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00 ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
             (2020-12-19 12:04:48.597222)  vcan0  064   [8]  F0 01 FF FF FF FF FF FF :: Wrong data size: 8 instead of 1 bytes
             (2020-12-19 12:04:56.805087)  vcan0  1F4   [4]  01 02 03 04 ::
            IO_DEBUG(
                IO_DEBUG_test_unsigned: 1,
                IO_DEBUG_test_enum: two,
                IO_DEBUG_test_signed: 3,
                IO_DEBUG_test_float: 2.0
            )
             (2020-12-19 12:04:59.085517)  vcan0  1F3   [3]  01 02 03 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", "--prune", TestFiles.Dbc("socialledge.dbc")], input, expected);
    }

    // ported from test_command_line.py::test_decode_timestamp_zero
    [Fact]
    public void Decode_zero_timestamps()
    {
        var input = """
             (000.000000)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00
             (002.047817)  vcan0  064   [8]  F0 01 FF FF FF FF FF FF
             (012.831664)  vcan0  1F4   [4]  01 02 03 04
             (015.679614)  vcan0  1F3   [3]  01 02 03
            """;

        var expected = """
             (000.000000)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00 ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
             (002.047817)  vcan0  064   [8]  F0 01 FF FF FF FF FF FF :: Wrong data size: 8 instead of 1 bytes
             (012.831664)  vcan0  1F4   [4]  01 02 03 04 ::
            IO_DEBUG(
                IO_DEBUG_test_unsigned: 1,
                IO_DEBUG_test_enum: two,
                IO_DEBUG_test_signed: 3,
                IO_DEBUG_test_float: 2.0
            )
             (015.679614)  vcan0  1F3   [3]  01 02 03 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", "--prune", TestFiles.Dbc("socialledge.dbc")], input, expected);
    }

    // ported from test_command_line.py::test_decode_can_fd
    [Fact]
    public void Decode_can_fd()
    {
        var input = """
              vcan0  12333 [064]  02 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
            """;

        var expected = """
              vcan0  12333 [064]  02 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ::
            CanFd(
                Fie: 2,
                Fas: 1
            )

            """;

        AssertDecode(["decode", TestFiles.Dbc("foobar.dbc")], input, expected);
    }

    // ported from test_command_line.py::test_decode_log_format
    [Fact]
    public void Decode_log_format()
    {
        var input = """
            (1594172461.968006) vcan0 0C8#F000000000000000
            (1594172462.968006) vcan0 0C8#F000000000000000 T
            (1594172463.968006) vcan0 0C8#F000000000000000 R
            (1594172462.126542) vcan0 064#F001FFFFFFFFFFFFFFFF
            (1594172462.127684) vcan0 ERROR

            (1594172462.356874) vcan0 1F4#01020304
            (1594172462.688432) vcan0 1F3#010203
            """;

        var expected = """
            (1594172461.968006) vcan0 0C8#F000000000000000 ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
            (1594172462.968006) vcan0 0C8#F000000000000000 T ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
            (1594172463.968006) vcan0 0C8#F000000000000000 R ::
            SENSOR_SONARS(
                SENSOR_SONARS_mux: 0,
                SENSOR_SONARS_err_count: 15,
                SENSOR_SONARS_left: 0.0,
                SENSOR_SONARS_middle: 0.0,
                SENSOR_SONARS_right: 0.0,
                SENSOR_SONARS_rear: 0.0
            )
            (1594172462.126542) vcan0 064#F001FFFFFFFFFFFFFFFF :: Wrong data size: 10 instead of 1 bytes
            (1594172462.127684) vcan0 ERROR

            (1594172462.356874) vcan0 1F4#01020304 ::
            IO_DEBUG(
                IO_DEBUG_test_unsigned: 1,
                IO_DEBUG_test_enum: two,
                IO_DEBUG_test_signed: 3,
                IO_DEBUG_test_float: 2.0
            )
            (1594172462.688432) vcan0 1F3#010203 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", "--prune", TestFiles.Dbc("socialledge.dbc")], input, expected);
    }

    // ported from test_command_line.py::test_single_line_decode
    [Fact]
    public void Decode_single_line()
    {
        var input = """
              vcan0  0C8   [8]  F0 00 00 00 00 00 00 00
              vcan0  064   [10]  F0 01 FF FF FF FF FF FF FF FF
              vcan0  ERROR

              vcan0  1F4   [4]  01 02 03 04
              vcan0  1F3   [3]  01 02 03
            """;

        var expected = """
              vcan0  0C8   [8]  F0 00 00 00 00 00 00 00 :: SENSOR_SONARS(SENSOR_SONARS_mux: 0, SENSOR_SONARS_err_count: 15, SENSOR_SONARS_left: 0.0, SENSOR_SONARS_middle: 0.0, SENSOR_SONARS_right: 0.0, SENSOR_SONARS_rear: 0.0)
              vcan0  064   [10]  F0 01 FF FF FF FF FF FF FF FF :: Wrong data size: 10 instead of 1 bytes
              vcan0  ERROR

              vcan0  1F4   [4]  01 02 03 04 :: IO_DEBUG(IO_DEBUG_test_unsigned: 1, IO_DEBUG_test_enum: two, IO_DEBUG_test_signed: 3, IO_DEBUG_test_float: 2.0)
              vcan0  1F3   [3]  01 02 03 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", "--prune", "--single-line", TestFiles.Dbc("socialledge.dbc")],
                     input, expected);
    }

    // ported from test_command_line.py::test_decode_single_line_muxed_data; the multi-line
    // variant (test_decode_muxed_data) uses the same frames and its rendering path is
    // already covered by the tests above, so it was not duplicated here
    [Fact]
    public void Decode_single_line_muxed_data()
    {
        var input = """
              vcan0  401   [6]  00 00 98 98 0B 00
              vcan0  401   [6]  01 00 9C 98 0A 00
              vcan0  401   [6]  02 00 B5 98 0A 00
              vcan0  401   [6]  03 00 9D 98 0A 00
              vcan0  401   [6]  04 00 CB 98 0B 00
              vcan0  401   [6]  05 00 C5 98 0B 00
              vcan0  401   [6]  06 00 35 9A EA 59
              vcan0  401   [6]  07 00 B1 98 FA 59
              vcan0  401   [6]  08 00 A5 98 0B 00
              vcan0  401   [6]  09 00 73 99 0C 00
              vcan0  401   [6]  0A 00 66 98 0B 00
              vcan0  401   [6]  0B 00 65 96 0B 00
              vcan0  401   [6]  0C 00 72 99 B3 5A
              vcan0  401   [6]  0D 00 04 99 9D 5A
              vcan0  401   [6]  0E 00 F8 9A C4 5A
              vcan0  401   [6]  0F 00 3B 9C 89 5A
              vcan0  401   [6]  10 00 8E 9A DE 5A
              vcan0  401   [6]  11 00 E8 9B DE 5A
              vcan0  401   [6]  12 00 D5 99 C9 59
              vcan0  401   [6]  13 00 EE 99 0D 5A
              vcan0  401   [6]  14 00 83 99 02 5A
              vcan0  401   [6]  15 00 97 99 12 5A
              vcan0  401   [6]  16 00 F6 99 0C 5A
              vcan0  401   [6]  17 00 0E 9B C4 59
              vcan0  401   [6]  18 00 68 9A 42 5A
              vcan0  401   [6]  19 00 83 99 22 5A
              vcan0  401   [6]  1A 00 85 99 3D 5A
              vcan0  401   [6]  1B 00 EF 99 2F 5A
              vcan0  401   [6]  1C 00 7E 99 50 5A
              vcan0  401   [6]  1D 00 39 9A 21 5A
              vcan0  401   [6]  1E 00 44 99 F9 59
              vcan0  401   [6]  1F 00 60 99 1B 5A
              vcan0  401   [6]  20 00 42 99 0A 5A
              vcan0  401   [6]  21 00 C3 9A 33 5A
              vcan0  401   [6]  22 00 3D 99 1A 5A
              vcan0  401   [6]  23 00 59 99 5C 5A
            """;

        var expected = """
              vcan0  401   [6]  00 00 98 98 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 0, MODULE_VOLTAGE_00: 39064, MODULE_TEMP_00: 11)
              vcan0  401   [6]  01 00 9C 98 0A 00 :: BATTERY_VT(BATTERY_VT_INDEX: 1, MODULE_VOLTAGE_01: 39068, MODULE_TEMP_01: 10)
              vcan0  401   [6]  02 00 B5 98 0A 00 :: BATTERY_VT(BATTERY_VT_INDEX: 2, MODULE_VOLTAGE_02: 39093, MODULE_TEMP_02: 10)
              vcan0  401   [6]  03 00 9D 98 0A 00 :: BATTERY_VT(BATTERY_VT_INDEX: 3, MODULE_VOLTAGE_03: 39069, MODULE_TEMP_03: 10)
              vcan0  401   [6]  04 00 CB 98 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 4, MODULE_VOLTAGE_04: 39115, MODULE_TEMP_04: 11)
              vcan0  401   [6]  05 00 C5 98 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 5, MODULE_VOLTAGE_05: 39109, MODULE_TEMP_05: 11)
              vcan0  401   [6]  06 00 35 9A EA 59 :: BATTERY_VT(BATTERY_VT_INDEX: 6, MODULE_VOLTAGE_06: 39477, MODULE_TEMP_06: 23018)
              vcan0  401   [6]  07 00 B1 98 FA 59 :: BATTERY_VT(BATTERY_VT_INDEX: 7, MODULE_VOLTAGE_07: 39089, MODULE_TEMP_07: 23034)
              vcan0  401   [6]  08 00 A5 98 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 8, MODULE_VOLTAGE_08: 39077, MODULE_TEMP_08: 11)
              vcan0  401   [6]  09 00 73 99 0C 00 :: BATTERY_VT(BATTERY_VT_INDEX: 9, MODULE_VOLTAGE_09: 39283, MODULE_TEMP_09: 12)
              vcan0  401   [6]  0A 00 66 98 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 10, MODULE_VOLTAGE_10: 39014, MODULE_TEMP_10: 11)
              vcan0  401   [6]  0B 00 65 96 0B 00 :: BATTERY_VT(BATTERY_VT_INDEX: 11, MODULE_VOLTAGE_11: 38501, MODULE_TEMP_11: 11)
              vcan0  401   [6]  0C 00 72 99 B3 5A :: BATTERY_VT(BATTERY_VT_INDEX: 12, MODULE_VOLTAGE_12: 39282, MODULE_TEMP_12: 23219)
              vcan0  401   [6]  0D 00 04 99 9D 5A :: BATTERY_VT(BATTERY_VT_INDEX: 13, MODULE_VOLTAGE_13: 39172, MODULE_TEMP_13: 23197)
              vcan0  401   [6]  0E 00 F8 9A C4 5A :: BATTERY_VT(BATTERY_VT_INDEX: 14, MODULE_VOLTAGE_14: 39672, MODULE_TEMP_14: 23236)
              vcan0  401   [6]  0F 00 3B 9C 89 5A :: BATTERY_VT(BATTERY_VT_INDEX: 15, MODULE_VOLTAGE_15: 39995, MODULE_TEMP_15: 23177)
              vcan0  401   [6]  10 00 8E 9A DE 5A :: BATTERY_VT(BATTERY_VT_INDEX: 16, MODULE_VOLTAGE_16: 39566, MODULE_TEMP_16: 23262)
              vcan0  401   [6]  11 00 E8 9B DE 5A :: BATTERY_VT(BATTERY_VT_INDEX: 17, MODULE_VOLTAGE_17: 39912, MODULE_TEMP_17: 23262)
              vcan0  401   [6]  12 00 D5 99 C9 59 :: BATTERY_VT(BATTERY_VT_INDEX: 18, MODULE_VOLTAGE_18: 39381, MODULE_TEMP_18: 22985)
              vcan0  401   [6]  13 00 EE 99 0D 5A :: BATTERY_VT(BATTERY_VT_INDEX: 19, MODULE_VOLTAGE_19: 39406, MODULE_TEMP_19: 23053)
              vcan0  401   [6]  14 00 83 99 02 5A :: BATTERY_VT(BATTERY_VT_INDEX: 20, MODULE_VOLTAGE_20: 39299, MODULE_TEMP_20: 23042)
              vcan0  401   [6]  15 00 97 99 12 5A :: BATTERY_VT(BATTERY_VT_INDEX: 21, MODULE_VOLTAGE_21: 39319, MODULE_TEMP_21: 23058)
              vcan0  401   [6]  16 00 F6 99 0C 5A :: BATTERY_VT(BATTERY_VT_INDEX: 22, MODULE_VOLTAGE_22: 39414, MODULE_TEMP_22: 23052)
              vcan0  401   [6]  17 00 0E 9B C4 59 :: BATTERY_VT(BATTERY_VT_INDEX: 23, MODULE_VOLTAGE_23: 39694, MODULE_TEMP_23: 22980)
              vcan0  401   [6]  18 00 68 9A 42 5A :: BATTERY_VT(BATTERY_VT_INDEX: 24, MODULE_VOLTAGE_24: 39528, MODULE_TEMP_24: 23106)
              vcan0  401   [6]  19 00 83 99 22 5A :: BATTERY_VT(BATTERY_VT_INDEX: 25, MODULE_VOLTAGE_25: 39299, MODULE_TEMP_25: 23074)
              vcan0  401   [6]  1A 00 85 99 3D 5A :: BATTERY_VT(BATTERY_VT_INDEX: 26, MODULE_VOLTAGE_26: 39301, MODULE_TEMP_26: 23101)
              vcan0  401   [6]  1B 00 EF 99 2F 5A :: BATTERY_VT(BATTERY_VT_INDEX: 27, MODULE_VOLTAGE_27: 39407, MODULE_TEMP_27: 23087)
              vcan0  401   [6]  1C 00 7E 99 50 5A :: BATTERY_VT(BATTERY_VT_INDEX: 28, MODULE_VOLTAGE_28: 39294, MODULE_TEMP_28: 23120)
              vcan0  401   [6]  1D 00 39 9A 21 5A :: BATTERY_VT(BATTERY_VT_INDEX: 29, MODULE_VOLTAGE_29: 39481, MODULE_TEMP_29: 23073)
              vcan0  401   [6]  1E 00 44 99 F9 59 :: BATTERY_VT(BATTERY_VT_INDEX: 30, MODULE_VOLTAGE_30: 39236, MODULE_TEMP_30: 23033)
              vcan0  401   [6]  1F 00 60 99 1B 5A :: BATTERY_VT(BATTERY_VT_INDEX: 31, MODULE_VOLTAGE_31: 39264, MODULE_TEMP_31: 23067)
              vcan0  401   [6]  20 00 42 99 0A 5A :: BATTERY_VT(BATTERY_VT_INDEX: 32, MODULE_VOLTAGE_32: 39234, MODULE_TEMP_32: 23050)
              vcan0  401   [6]  21 00 C3 9A 33 5A :: BATTERY_VT(BATTERY_VT_INDEX: 33, MODULE_VOLTAGE_33: 39619, MODULE_TEMP_33: 23091)
              vcan0  401   [6]  22 00 3D 99 1A 5A :: BATTERY_VT(BATTERY_VT_INDEX: 34, MODULE_VOLTAGE_34: 39229, MODULE_TEMP_34: 23066)
              vcan0  401   [6]  23 00 59 99 5C 5A :: BATTERY_VT(BATTERY_VT_INDEX: 35, MODULE_VOLTAGE_35: 39257, MODULE_TEMP_35: 23132)

            """;

        AssertDecode(["decode", "--single-line", TestFiles.Dbc("msxii_system_can.dbc")],
                     input, expected);
    }

    // ported from test_command_line.py::test_single_line_decode_log_format
    [Fact]
    public void Decode_single_line_log_format()
    {
        var input = """
            (1594172461.968006) vcan0 0C8#F000000000000000
            (1594172462.126542) vcan0 064#F001FFFFFFFFFFFFFFFF
            (1594172462.127684) vcan0 ERROR

            (1594172462.356874) vcan0 1F4#01020304
            (1594172462.688432) vcan0 1F3#010203
            """;

        var expected = """
            (1594172461.968006) vcan0 0C8#F000000000000000 :: SENSOR_SONARS(SENSOR_SONARS_mux: 0, SENSOR_SONARS_err_count: 15, SENSOR_SONARS_left: 0.0, SENSOR_SONARS_middle: 0.0, SENSOR_SONARS_right: 0.0, SENSOR_SONARS_rear: 0.0)
            (1594172462.126542) vcan0 064#F001FFFFFFFFFFFFFFFF :: Wrong data size: 10 instead of 1 bytes
            (1594172462.127684) vcan0 ERROR

            (1594172462.356874) vcan0 1F4#01020304 :: IO_DEBUG(IO_DEBUG_test_unsigned: 1, IO_DEBUG_test_enum: two, IO_DEBUG_test_signed: 3, IO_DEBUG_test_float: 2.0)
            (1594172462.688432) vcan0 1F3#010203 :: Unknown frame id 499 (0x1f3)

            """;

        AssertDecode(["decode", "--prune", "--single-line", TestFiles.Dbc("socialledge.dbc")],
                     input, expected);
    }
}
