namespace GESCore;

public class CPU
{
    public enum State
    {
        FetchOpcode,
        FetchAddressLow,
        FetchAddressHigh,
        ReadFromAddress,
    }

    private State _state = State.FetchOpcode;

    private bool _addressCarry;

    private byte _a, _x, _y, _p, _sp, _operand;

    private byte _opcode;
    private byte _addressLow;
    private byte _addressHigh;

    private ushort _pc;

    private readonly MMU _mmu = new();

    public void Tick()
    {
        switch (_state)
        {
            case State.FetchOpcode:
                {
                    _opcode = _mmu.ReadByte(_pc++);
                    int b = _opcode & 0x1C;
                    int c = _opcode & 0x03;
                    if (b == 0x08 || b == 0x10 || (b == 0x00 && (c == 0x00 || c == 0x02)) || (b == 0x18 && (c == 0x00 || c == 0x02))) // instruction uses a single operand or it is implied
                    {
                        _state = State.ReadFromAddress;
                        break;
                    }
                    _state = State.FetchAddressLow;
                    break;
                }
            case State.FetchAddressLow:
                {
                    _addressLow = _mmu.ReadByte(_pc++);
                    int b = _opcode & 0x1C;
                    if (b == 0x0C || b == 0x1C)
                    {
                        _state = State.FetchAddressHigh;
                        break;
                    }
                    _state = State.ReadFromAddress;
                    break;
                }
            case State.FetchAddressHigh:
                {
                    _addressHigh = _mmu.ReadByte(_pc++);
                    int a = _opcode & 0xE0;
                    int b = _opcode & 0x1C;
                    int c = _opcode & 0x03;
                    if (b == 0x1C)
                    {
                        if (c == 0x02 && (a == 0x80 || a == 0xA0))
                        {
                            _addressCarry = ((_addressLow + _y) >> 8) != 0;
                            _addressLow += _y;

                        }
                        else
                        {
                            _addressCarry = ((_addressLow + _y) >> 8) != 0;
                            _addressLow += _x;
                        }
                    }

                    _state = State.ReadFromAddress;
                    break;
                }
            case State.ReadFromAddress:
                {
                    int a = _opcode & 0xE0;
                    int b = _opcode & 0x1C;
                    int c = _opcode & 0x03;

                    _operand = _mmu.ReadByte((ushort)((_addressHigh << 8) | _addressLow));

                    if (b == 0x14)
                    {
                        if (c == 0x02 && (a == 0x80 || a == 0xA0))
                        {
                            _addressLow += _y;
                        }
                        else
                        {
                            _addressLow += _x;
                        }
                    }
                    else if (_addressCarry)
                    {
                        _addressCarry = false;
                        _addressHigh++;
                    }
                    else
                    {
                        _state = State.FetchOpcode;
                    }
                    break;
                }
        }
    }
}
