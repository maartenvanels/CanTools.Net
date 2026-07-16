using CanTools.Cli;

namespace CanTools.Tests.Cli;

public class DumpCommandTests
{
    private const string Blue = "\x1b[94m";
    private const string Reset = "\x1b[0m";

    public DumpCommandTests()
    {
        // upstream fakes an 80 column screen for the --with-comments tests
        DumpCommand.ConsoleWidthOverride = () => 80;
    }

    private static void AssertDump(string[] args, string expected)
    {
        var (exitCode, stdout, stderr) = CliRunner.Run(args);

        Assert.Equal("", stderr);
        Assert.Equal(0, exitCode);
        // raw string literals inherit the source file's line endings, which git may
        // check out as CRLF; the CLI always writes LF
        Assert.Equal(expected.ReplaceLineEndings("\n"), stdout);
    }

    // ported from test_command_line.py::test_dump
    [Fact]
    public void Dump_motohawk()
    {
        var expected = """
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           ExampleMessage
              Id:             0x1f0
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        PCM1
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |<-x|<---------------------x|<--|
                       +---+---+---+---+---+---+---+---+
                         |                       +-- AverageRadius
                         +-- Enable
                       +---+---+---+---+---+---+---+---+
                     1 |-------------------------------|
                       +---+---+---+---+---+---+---+---+
                     2 |----------x|   |   |   |   |   |
                 B     +---+---+---+---+---+---+---+---+
                 y               +-- Temperature
                 t     +---+---+---+---+---+---+---+---+
                 e   3 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     4 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- Enable
                   +-- AverageRadius
                   +-- Temperature

              Signal choices:

                Enable
                    0 Disabled
                    1 Enabled

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", TestFiles.Dbc("motohawk.dbc")], expected);
    }

    // ported from test_command_line.py::test_dump_with_comments
    [Fact]
    public void Dump_with_comments()
    {
        var expected = $$"""
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           ExampleMessage
              Id:             0x1f0
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        PCM1
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |<-x|<---------------------x|<--|
                       +---+---+---+---+---+---+---+---+
                         |                       +-- AverageRadius
                         +-- Enable
                       +---+---+---+---+---+---+---+---+
                     1 |-------------------------------|
                       +---+---+---+---+---+---+---+---+
                     2 |----------x|   |   |   |   |   |
                 B     +---+---+---+---+---+---+---+---+
                 y               +-- Temperature
                 t     +---+---+---+---+---+---+---+---+
                 e   3 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     4 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- Enable {{Blue}}Enable signal comment [-]{{Reset}}
                   +-- AverageRadius {{Blue}}AverageRadius signal comment [m]{{Reset}}
                   +-- Temperature {{Blue}}Temperature with a really long and complicated comment
                                   that probably require many many lines in a decently wide
                                   terminal [degK]{{Reset}}

              Signal choices:

                Enable
                    0 Disabled
                    1 Enabled

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", "--with-comments", TestFiles.Dbc("motohawk_with_comments.dbc")],
                   expected);
    }

    // ported from test_command_line.py::test_dump_with_comments_mux
    [Fact]
    public void Dump_with_comments_mux()
    {
        var expected = $$"""
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           Message1
              Id:             0x123456
                  Priority:       0
                  PGN:            0x01200
                  Source:         0x56
                  Destination:    0x34
                  Format:         PDU 1
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        -
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |<---------------------x|   |   |
                       +---+---+---+---+---+---+---+---+
                         +-- Multiplexor
                       +---+---+---+---+---+---+---+---+
                     1 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     2 |<-x|   |   |   |<-x|<-x|   |   |
                       +---+---+---+---+---+---+---+---+
                         |               |   +-- BIT_J
                         |               +-- BIT_C
                         +-- BIT_G
                       +---+---+---+---+---+---+---+---+
                     3 |   |   |<-x|<-x|   |<-x|   |<-x|
                 B     +---+---+---+---+---+---+---+---+
                 y               |   |       |       +-- BIT_L
                 t               |   |       +-- BIT_A
                 e               |   +-- BIT_K
                                 +-- BIT_E
                       +---+---+---+---+---+---+---+---+
                     4 |<-x|<-x|   |   |   |   |<-x|<-x|
                       +---+---+---+---+---+---+---+---+
                         |   |                   |   +-- BIT_D
                         |   |                   +-- BIT_B
                         |   +-- BIT_H
                         +-- BIT_F
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- Multiplexor {{Blue}}Defines data content for response messages.{{Reset}}
                       +-- 8
                       |   +-- BIT_J
                       |   +-- BIT_C
                       |   +-- BIT_G
                       |   +-- BIT_L
                       +-- 16
                       |   +-- BIT_J
                       |   +-- BIT_C
                       |   +-- BIT_G
                       |   +-- BIT_L
                       +-- 24
                           +-- BIT_J
                           +-- BIT_C
                           +-- BIT_G
                           +-- BIT_L
                           +-- BIT_A
                           +-- BIT_K
                           +-- BIT_E
                           +-- BIT_D
                           +-- BIT_B
                           +-- BIT_H
                           +-- BIT_F

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", "--with-comments", TestFiles.Dbc("bus_comment.dbc")], expected);
    }

    // ported from test_command_line.py::test_dump_no_sender
    [Fact]
    public void Dump_no_sender()
    {
        var expected = """
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           Foo
              Id:             0x1d8
              Length:         1 bytes
              Cycle time:     - ms
              Senders:        -
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                 B   0 |<-----------------------------x|
                 y     +---+---+---+---+---+---+---+---+
                 t       +-- signal_without_sender
                 e

              Signal tree:

                -- {root}
                   +-- signal_without_sender

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", "--no-strict", TestFiles.Dbc("no_sender.dbc")], expected);
    }

    // ported from test_command_line.py::test_dump_signal_choices
    [Fact]
    public void Dump_signal_choices()
    {
        var expected = """
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           Message0
              Id:             0x400
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        Node0
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |   |   |   |<---------x|<-----x|
                       +---+---+---+---+---+---+---+---+
                                     |           +-- FooSignal
                                     +-- BarSignal
                       +---+---+---+---+---+---+---+---+
                     1 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                 B   2 |   |   |   |   |   |   |   |   |
                 y     +---+---+---+---+---+---+---+---+
                 t   3 |   |   |   |   |   |   |   |   |
                 e     +---+---+---+---+---+---+---+---+
                     4 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- FooSignal
                   +-- BarSignal

              Signal choices:

                FooSignal
                    0 A
                    1 B
                    2 C
                    3 D

                BarSignal
                    0 A
                    1 B
                    2 C
                    3 D
                    4 E
                    5 F
                    6 G
                    7 H

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", "--prune", TestFiles.Dbc("dump_signal_choices.dbc")], expected);
    }

    // ported from test_command_line.py::test_dump_j1939
    [Fact]
    public void Dump_j1939()
    {
        var expected = """
            ================================= Messages =================================

              ------------------------------------------------------------------------

              Name:           Message1
              Id:             0x15340201
                  Priority:       5
                  PGN:            0x13400
                  Source:         0x01
                  Destination:    0x02
                  Format:         PDU 1
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        Node1
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |<-----------------------------x|
                       +---+---+---+---+---+---+---+---+
                         +-- Signal1
                       +---+---+---+---+---+---+---+---+
                     1 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                 B   2 |   |   |   |   |   |   |   |   |
                 y     +---+---+---+---+---+---+---+---+
                 t   3 |   |   |   |   |   |   |   |   |
                 e     +---+---+---+---+---+---+---+---+
                     4 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- Signal1

              ------------------------------------------------------------------------

              Name:           Message2
              Id:             0x15f01002
                  Priority:       5
                  PGN:            0x1f010
                  Source:         0x02
                  Destination:    All
                  Format:         PDU 2
              Length:         8 bytes
              Cycle time:     - ms
              Senders:        Node2
              Layout:

                                      Bit

                         7   6   5   4   3   2   1   0
                       +---+---+---+---+---+---+---+---+
                     0 |<-----------------------------x|
                       +---+---+---+---+---+---+---+---+
                         +-- Signal2
                       +---+---+---+---+---+---+---+---+
                     1 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                 B   2 |   |   |   |   |   |   |   |   |
                 y     +---+---+---+---+---+---+---+---+
                 t   3 |   |   |   |   |   |   |   |   |
                 e     +---+---+---+---+---+---+---+---+
                     4 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     5 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     6 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+
                     7 |   |   |   |   |   |   |   |   |
                       +---+---+---+---+---+---+---+---+

              Signal tree:

                -- {root}
                   +-- Signal2

              ------------------------------------------------------------------------

            """;

        AssertDump(["dump", TestFiles.Dbc("j1939.dbc")], expected);
    }
}
