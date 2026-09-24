namespace GESCore;

public class CPU
{
    public enum State
    {
        InstructionFetch,
        OperandFetch,
        InstructionExecute,
    }

    private State _state = State.InstructionFetch;

    private byte _a, _x, _y, _p, _sp;

    private readonly byte[] _instructionData = new byte[3];

    private ushort _pc;

    private readonly MMU _mmu = new();

    public void Tick()
    {
        switch (_state)
        {
            case State.InstructionFetch:
                _instructionData[0] = _mmu.ReadByte(_pc++);
                if ((_instructionData[0] & 0b1101) == 0b1000 || _instructionData[0] == 0x00 || _instructionData[0] == 0x40 || _instructionData[0] == 0x60)
                {
                    _state = State.InstructionExecute;
                    break;
                }
                _state = State.OperandFetch;
                break;
            case State.OperandFetch:
                _instructionData[1] = _mmu.ReadByte(_pc++);
                break;
            case State.InstructionExecute:
                break;
        }
    }
}
