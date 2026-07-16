namespace CanTools.Tests.Cli;

// The upstream ARXML cases (test_arxml3, test_arxml4) are not ported: the ARXML
// reader is out of scope, see PLAN.md.
public class ListCommandTests
{
    private static void AssertList(string[] args, string expected)
    {
        var (exitCode, stdout, stderr) = CliRunner.Run(args);

        Assert.Equal("", stderr);
        Assert.Equal(0, exitCode);
        // raw string literals inherit the source file's line endings, which git may
        // check out as CRLF; the CLI always writes LF
        Assert.Equal(expected.ReplaceLineEndings("\n"), stdout);
    }

    // ported from test_list.py::test_dbc (message name listing)
    [Fact]
    public void List_dbc_message_names()
    {
        AssertList(["list", "--prune", TestFiles.Dbc("motohawk.dbc")], "ExampleMessage\n");
    }

    // ported from test_list.py::test_dbc (message details via --all)
    [Fact]
    public void List_dbc_message_details()
    {
        var expected = """
            ExampleMessage:
              Comment[None]: Example message used as template in MotoHawk models.
              Sending ECUs: PCM1
              Frame ID: 0x1f0 (496)
              Size: 8 bytes
              Is extended frame: False
              Is CAN-FD frame: False
              Signal tree:

                -- {root}
                   +-- Enable
                   +-- AverageRadius
                   +-- Temperature

              Signal details:
                Enable:
                  Internal type: Integer
                  Start bit: 7
                  Length: 1 bits
                  Byte order: big_endian
                  Unit: -
                  Is signed: False
                  Named values:
                    0: Disabled
                    1: Enabled
                AverageRadius:
                  Internal type: Integer
                  Start bit: 6
                  Length: 6 bits
                  Byte order: big_endian
                  Unit: m
                  Is signed: False
                  Minimum: 0 m
                  Maximum: 5 m
                  Offset: 0 m
                  Scaling factor: 0.1 m
                Temperature:
                  Receiving ECUs: FOO, PCM1
                  Internal type: Integer
                  Start bit: 0
                  Length: 12 bits
                  Byte order: big_endian
                  Unit: degK
                  Is signed: True
                  Minimum: 229.52 degK
                  Maximum: 270.47 degK
                  Offset: 250 degK
                  Scaling factor: 0.01 degK

            """;

        AssertList(["list", "--prune", "--all", TestFiles.Dbc("motohawk.dbc")], expected);
    }

    // ported from test_list.py::test_kcd (normal frames via --all -x)
    [Fact]
    public void List_kcd_normal_frame_details()
    {
        var expected = """
            Message1:
              Bus: Bus
              Sending ECUs: Node1
              Frame ID: 0x1 (1)
              Size: 5 bytes
              Is extended frame: False
              Is CAN-FD frame: False
              Signal tree:

                -- {root}
                   +-- Signal1
                   +-- Signal2

              Signal details:
                Signal1:
                  Internal type: Integer
                  Start bit: 0
                  Length: 1 bits
                  Byte order: little_endian
                  Is signed: False
                Signal2:
                  Receiving ECUs: Node2, Node3
                  Internal type: Float
                  Start bit: 8
                  Length: 32 bits
                  Byte order: little_endian
                  Is signed: False
                  Named values:
                    0: label1
                    1: label2
            Message2:
              Comment[None]: Note message 2.
              Bus: Bus
              Sending ECUs: Node2, Node3
              Frame ID: 0x2 (2)
              Size: 4 bytes
              Is extended frame: False
              Is CAN-FD frame: False
              Cycle time: 100 ms
              Signal tree:

                -- {root}
                   +-- Mux1
                   |   +-- 0
                   |   |   +-- Signal1
                   |   |   +-- Signal2
                   |   +-- 1
                   |       +-- Signal3
                   |       +-- Signal4
                   +-- Mux2
                   |   +-- 0
                   |       +-- Signal5
                   +-- Signal6

              Signal details:
                Signal1:
                  Internal type: Integer
                  Selector signal: Mux1
                  Selector values: 0
                  Start bit: 0
                  Length: 8 bits
                  Byte order: little_endian
                  Is signed: False
                Signal3:
                  Internal type: Integer
                  Selector signal: Mux1
                  Selector values: 1
                  Start bit: 0
                  Length: 8 bits
                  Byte order: little_endian
                  Is signed: False
                Signal2:
                  Internal type: Integer
                  Selector signal: Mux1
                  Selector values: 0
                  Start bit: 8
                  Length: 8 bits
                  Byte order: little_endian
                  Is signed: False
                Signal4:
                  Internal type: Integer
                  Selector signal: Mux1
                  Selector values: 1
                  Start bit: 8
                  Length: 8 bits
                  Byte order: little_endian
                  Is signed: False
                Mux1:
                  Internal type: Multiplex Selector
                  Start bit: 16
                  Length: 2 bits
                  Byte order: little_endian
                  Is signed: False
                Mux2:
                  Internal type: Multiplex Selector
                  Start bit: 18
                  Length: 1 bits
                  Byte order: little_endian
                  Is signed: False
                Signal5:
                  Internal type: Integer
                  Selector signal: Mux2
                  Selector values: 0
                  Start bit: 19
                  Length: 1 bits
                  Byte order: little_endian
                  Is signed: False
                Signal6:
                  Comment[None]: Note signal 6.
                  Receiving ECUs: Node1
                  Internal type: Integer
                  Start bit: 20
                  Length: 12 bits
                  Byte order: little_endian
                  Unit: Cel
                  Is signed: True
                  Minimum: 0 Cel
                  Maximum: 100 Cel
                  Offset: -40 Cel
                  Scaling factor: 0.05 Cel
                  Named values:
                    0: init
            Message4:
              Bus: Bus
              Frame ID: 0x4 (4)
              Size: 5 bytes
              Is extended frame: False
              Is CAN-FD frame: False
              Signal tree:

                -- {root}
                   +-- Signal1
                   +-- Signal2

              Signal details:
                Signal1:
                  Internal type: Integer
                  Start bit: 7
                  Length: 1 bits
                  Byte order: big_endian
                  Is signed: False
                Signal2:
                  Internal type: Integer
                  Start bit: 8
                  Length: 12 bits
                  Byte order: big_endian
                  Is signed: False

            """;

        AssertList(["list", "--prune", "--all", "-x", TestFiles.Kcd("dump.kcd")], expected);
    }

    // ported from test_list.py::test_kcd (extended frames via --all -n)
    [Fact]
    public void List_kcd_extended_frame_details()
    {
        var expected = """
            Message3:
              Bus: Bus
              Frame ID: 0x3 (3)
              Size: 8 bytes
              Is extended frame: True
              Is CAN-FD frame: False
              Signal tree:

                -- {root}
                   +-- Signal1

              Signal details:
                Signal1:
                  Internal type: Float
                  Start bit: 0
                  Length: 64 bits
                  Byte order: little_endian
                  Is signed: False

            """;

        AssertList(["list", "--prune", "--all", "-n", TestFiles.Kcd("dump.kcd")], expected);
    }

    // ported from test_list.py::test_kcd (both frame kinds excluded)
    [Fact]
    public void List_kcd_all_frames_excluded()
    {
        AssertList(["list", "--prune", "--all", "-n", "-x", TestFiles.Kcd("dump.kcd")], "");
    }
}
