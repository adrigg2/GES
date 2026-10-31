using System.ComponentModel;

namespace GESCore;

public class CPU
{
    public enum State
    {
        FetchOpcode,
        FetchAddressLow,
        FetchAddressHigh,
        FetchOperand,
        IndexPointer,
        FetchAddressLowFromIndexedPointer,
        FetchAddressHighFromIndexedPointer,
        FetchIndexedAddressLowFromPointer,
        FetchIndexedAddressHighFromPointer,
        ReadFromAddress,
        ExecuteImplied,
    }

    [Flags]
    private enum CPUFlags
    {
        Carry            = 0b0000_0001,
        Zero             = 0b0000_0010,
        InterruptDisable = 0b0000_0100,
        Decimal          = 0b0000_1000,
        B                = 0b0001_0000,
        Overflow         = 0b0100_0000,
        Negative         = 0b1000_0000
    }

    private enum OpcodeType : byte
    {
        Control = 0b0000_0000,
        ALU     = 0b0000_0001,
        RMW     = 0b0000_0010,
    }

    private enum OpcodeAddressMode : byte
    {
        ImplImmInd  = 0b0000_0000,
        ZPG         = 0b0000_0100,
        ImmImpl     = 0b0000_1000,
        Abs         = 0b0000_1100,
        RelInd      = 0b0001_0000,
        ZPGX        = 0b0001_0100,
        ImplAbsY    = 0b0001_1000,
        AbsX        = 0b0001_1100,
    }

    private State _state = State.FetchOpcode;

    private bool _addressCarry;

    private byte _a, _x, _y, _p, _sp, _operand;

    private byte _opcode;
    private byte _addressLow;
    private byte _addressHigh;

    private ushort _pc;

    private readonly MMU _mmu = new();

    public CPU()
    {
        _p = 0x20;
    }

    public void Tick()
    {
        int a = _opcode & 0xE0;
        int b = _opcode & 0x1C;
        int c = _opcode & 0x03;

        switch (_state)
        {
            case State.FetchOpcode:
                _opcode = _mmu.ReadByte(_pc++);
                a = _opcode & 0xE0;
                b = _opcode & 0x1C;
                c = _opcode & 0x03;
                if ((b == (byte)OpcodeAddressMode.ImmImpl && c == (byte)OpcodeType.ALU) || (b == (byte)OpcodeAddressMode.ImplImmInd && (c == (byte)OpcodeType.Control || c == (byte)OpcodeType.RMW) && a >= 0x80) || (c == (byte)OpcodeType.Control && b == (byte)OpcodeAddressMode.RelInd)) // instruction uses a single operand
                {
                    _state = State.FetchOperand;
                    break;
                }
                else if ((c == (byte)OpcodeType.Control || c == (byte)OpcodeType.RMW) && (b == (byte)OpcodeAddressMode.ImmImpl || b == (byte)OpcodeAddressMode.ImplAbsY || (b == (byte)OpcodeAddressMode.ImplImmInd && a < 0x80)))
                {
                    _state = State.ExecuteImplied;
                    break;
                }
                _state = State.FetchAddressLow;
                break;
            case State.FetchAddressLow:
                _addressLow = _mmu.ReadByte(_pc++);
                if (b == (byte)OpcodeAddressMode.Abs || b == (byte)OpcodeAddressMode.AbsX || (b == (byte)OpcodeAddressMode.ImplAbsY && c == (byte)OpcodeType.ALU))
                {
                    _state = State.FetchAddressHigh;
                    break;
                }
                else if (b == (byte)OpcodeAddressMode.ImplImmInd)
                {
                    _state = State.IndexPointer;
                    break;
                }
                else if (b == (byte)OpcodeAddressMode.RelInd)
                {
                    _state = State.FetchIndexedAddressLowFromPointer;
                    break;
                }
                _state = State.ReadFromAddress;
                break;
            case State.FetchAddressHigh:
                _addressHigh = _mmu.ReadByte(_pc++);
                if (b == (byte)OpcodeAddressMode.AbsX)
                {
                    if ((c == (byte)OpcodeType.RMW && (a == 0x80 || a == 0xA0)) || b == (byte)OpcodeAddressMode.ImplAbsY)
                    {
                        _addressCarry = ((_addressLow + _y) >> 8) != 0;
                        _addressLow += _y;
                    }
                    else
                    {
                        _addressCarry = ((_addressLow + _x) >> 8) != 0;
                        _addressLow += _x;
                    }
                }

                _state = State.ReadFromAddress;
                break;
            case State.FetchOperand:
                _operand = _mmu.ReadByte(_pc++);
                switch (c)
                {
                    case (byte)OpcodeType.Control:
                        break;
                    case (byte)OpcodeType.ALU:
                        ALUInstruction(a, true);
                        break;
                    case (byte)OpcodeType.RMW:
                        break;
                }
                break;
            case State.IndexPointer:
                _addressLow = (byte)(_addressLow + _x);
                _state = State.FetchAddressLowFromIndexedPointer;
                break;
            case State.FetchAddressLowFromIndexedPointer:
                _operand = _mmu.ReadByte(_addressLow);
                _state = State.FetchAddressHighFromIndexedPointer;
                break;
            case State.FetchAddressHighFromIndexedPointer:
                _addressHigh = _mmu.ReadByte((byte)(_addressLow + 1));
                _addressLow = _operand;
                _state = State.ReadFromAddress;
                break;
            case State.FetchIndexedAddressLowFromPointer:
                _operand = _addressLow;
                _addressLow = _mmu.ReadByte(_operand);
                _state = State.FetchIndexedAddressHighFromPointer;
                break;
            case State.FetchIndexedAddressHighFromPointer:
                _addressHigh = _mmu.ReadByte((byte)(_operand + 1));
                _addressCarry = ((_addressLow + _y) >> 8) != 0;
                _addressLow += _y;
                _state = State.ReadFromAddress;
                break;
            case State.ReadFromAddress:
                _operand = _mmu.ReadByte((ushort)((_addressHigh << 8) | _addressLow));

                if (b == (byte)OpcodeAddressMode.ZPGX)
                {
                    if (c == (byte)OpcodeType.RMW && (a == 0x80 || a == 0xA0))
                    {
                        _addressLow += _y;
                    }
                    else
                    {
                        _addressLow += _x;
                    }
                    break;
                }
                else if (_addressCarry)
                {
                    _addressCarry = false;
                    _addressHigh++;
                    break;
                }

                switch (c)
                {
                    case (byte)OpcodeType.Control:
                        break;
                    case (byte)OpcodeType.ALU:
                        ALUInstruction(a);
                        break;
                    case (byte)OpcodeType.RMW:
                        break;
                }
                _state = State.FetchOpcode;
                break;
            case State.ExecuteImplied:
                break;
        }
    }

    private void ALUInstruction(int a, bool immediateAddressing = false)
    {
        switch(a >> 5)
        {
            case 0x00:
                ORA();
                break;
            case 0x01:
                AND();
                break;
            case 0x02:
                EOR();
                break;
            case 0x03:
                ADC();
                break;
            case 0x04:
                if (!immediateAddressing)
                {
                    _mmu.WriteByte((ushort)((_addressHigh << 8) | _addressLow), _a);
                }
                break;
            case 0x05:
                _a = _operand;
                SetZeroFlag(_a);
                SetNegativeFlag(_a);
                break;
            case 0x06:
                CMP();
                break;
            case 0x07:
                SBC();
                break;
        }
    }

    private void ORA()
    {
        _a |= _operand;
        SetZeroFlag(_a);
        SetNegativeFlag(_a);
    }

    private void AND()
    {
        _a &= _operand;
        SetZeroFlag(_a);
        SetNegativeFlag(_a);
    }

    private void EOR()
    {
        _a ^= _operand;
        SetZeroFlag(_a);
        SetNegativeFlag(_a);
    }

    private void ADC()
    {
        int carryValue = ((CPUFlags)_p).HasFlag(CPUFlags.Carry) ? 1 : 0;
        int result = _a + _operand + carryValue;
        SetOverflowFlag(result);
        _a = (byte)result;
        SetCarryFlag(result);
        SetZeroFlag(_a);
        SetNegativeFlag(_a);
    }

    private void CMP()
    {
        int result = _a - _operand;
        SetCarryFlag(result);
        SetZeroFlag(result);
        SetNegativeFlag(result);
    }

    private void SBC()
    {
        _operand = (byte)~_operand;
        ADC();
    }

    private void ASL()
    {
        int result = _operand << 1;
        SetCarryFlag(result);
        SetZeroFlag(result);
        SetNegativeFlag(result);
        _operand = (byte)result;
    }

    private void ROL()
    {
        int result = (_operand << 1) | ((_p & (byte)CPUFlags.Carry) != 0 ? 1 : 0);
        SetCarryFlag(result);
        SetZeroFlag(result);
        SetNegativeFlag(result);
        _operand = (byte)result;
    }

    private void LSR()
    {
        int carry = _operand & 0x01;
        if (carry != 0)
        {
            _p |= (byte)CPUFlags.Carry;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Carry);
        }

        _operand = (byte)(_operand >> 1);
        SetZeroFlag(_operand);
        SetNegativeFlag(_operand);
    }

    private void ROR()
    {
        int result = (_operand >> 1) | ((_p & (byte)CPUFlags.Carry) != 0 ? 0x80 : 0);
        int carry = _operand & 0x01;
        if (carry != 0)
        {
            _p |= (byte)CPUFlags.Carry;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Carry);
        }

        SetZeroFlag(result);
        SetNegativeFlag(result);
        _operand = (byte)result;
    }

    private void SetCarryFlag(int value)
    {
        if (value > 0xFF)
        {
            _p |= (byte)CPUFlags.Carry;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Carry);
        }
    }

    private void SetZeroFlag(int value)
    {
        if ((value & 0xFF) == 0)
        {
            _p |= (byte)CPUFlags.Zero;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Zero);
        }
    }

    private void SetOverflowFlag(int value)
    {
        if (((value ^ _a) & (value ^ _operand) & 0x80) != 0)
        {
            _p |= (byte)CPUFlags.Overflow;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Overflow);
        }
    }

    private void SetNegativeFlag(int value)
    {
        if ((value & 0x80) != 0)
        {
            _p |= (byte)CPUFlags.Negative;
        }
        else
        {
            _p = (byte)(_p & ~(byte)CPUFlags.Negative);
        }
    }
}
