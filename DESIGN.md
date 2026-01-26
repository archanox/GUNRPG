# Design Document Summary

## System Overview

GUNRPG implements a discrete event simulation combat system where actions are resolved through deterministic time-based events rather than traditional turn-based mechanics.

## Key Design Decisions

### 1. Time Model

**Decision**: Use milliseconds as the canonical time unit.

**Rationale**: 
- Allows 1:1 use of real weapon stats (fire rate, reload time, etc.)
- Sufficient precision for tactical decisions
- Matches game industry standards

### 2. Event Queue System

**Decision**: Use a priority queue sorted by timestamp → operator ID → sequence number.

**Rationale**:
- Deterministic ordering ensures reproducibility
- Allows for precise scheduling of future events
- Operator ID as secondary sort maintains fairness
- Sequence numbers prevent ambiguity

### 3. Intent-Based Actions

**Decision**: Actions are declarative intents that schedule future events, not immediate effects.

**Rationale**:
- Separates player decision-making from execution
- Allows for validation before committing
- Enables cancellation during planning phases
- Creates "planning" vs "execution" phase mental model

### 4. Commitment Units & Reaction Windows

**Decision**: Reaction opportunities occur after discrete outcomes (N bullets fired / meters moved), not time intervals.

**Rationale**:
- Intuitive to players ("react every 3 shots")
- Scales naturally with weapon types (SMGs = more frequent reactions)
- Respects realism (actual events, not arbitrary timers)
- Prevents "mag dump" scenarios with no counterplay

### 5. State-Based Validation

**Decision**: All actions validate against current operator state before execution.

**Rationale**:
- Clear error messages ("cannot fire: reloading")
- Prevents impossible actions
- Teaches players the rules organically
- No hidden state or "try and see what happens"

### 6. Health Regeneration

**Decision**: Call of Duty-style health regen with delay after damage.

**Rationale**:
- Rewards tactical positioning
- Creates tension ("can I survive to regen?")
- Simplifies long-term state management
- Proven design from successful games

### 7. Stamina vs Fatigue Separation

**Decision**: Stamina is combat-scoped (sprint/slide), Fatigue is persistent (readiness).

**Rationale**:
- Clear separation of concerns
- Stamina = mechanical resource (moment-to-moment)
- Fatigue = tactical readiness (between combats)
- No overlap or confusion

### 8. Distance-Only Spatial Model

**Decision**: Represent space as scalar distance, not 2D/3D positioning.

**Rationale**:
- Sufficient for 1v1 tactical decisions
- Simplifies movement calculations
- Reduces complexity without losing depth
- Easier to visualize in text interface

## System Architecture

### Module Structure

```
Time → Events → Intents → Combat → Operators
                              ↓
                            Weapons
```

Each module has single responsibility:
- **Time**: Global clock
- **Events**: Scheduling and execution
- **Intents**: Action validation and planning
- **Combat**: Orchestration and phase management
- **Operators**: State and statistics
- **Weapons**: Configuration and calculations

### Data Flow

1. Player submits Intent
2. Intent validates against Operator state
3. Combat System schedules Events
4. Events execute at timestamps
5. Events modify Operator state
6. Reaction Windows trigger return to Planning

## Testing Strategy

### Unit Tests

- **Time System**: Clock advancement, reset
- **Event Queue**: Ordering, removal, determinism
- **Operators**: State management, regeneration, damage
- **Weapons**: Damage calculation, falloff, headshots
- **Combat**: Intent submission, phase transitions
- **Rest System**: Fatigue, recovery, readiness

### Integration Tests

- Combat system with deterministic seed
- Multi-round combat scenarios
- AI decision making

## Performance Considerations

### Event Queue

- SortedSet provides O(log n) insertion/removal
- Sufficient for single-combat scenarios
- Could optimize with custom heap for larger scale

### State Updates

- Regeneration calculated on-demand during time advancement
- No per-frame updates
- Minimal overhead

## Future Extensions

### Cover System

Would add:
- Cover positions at specific distances
- Line-of-sight checks
- Intent visibility rules
- Flanking mechanics

### Multiple Opponents

Would require:
- Extended spatial model (2D positioning)
- Target selection in intents
- Multi-way reaction windows
- Area effects

### Equipment System

Would add:
- Weapon attachments (scopes, grips)
- Armor with damage reduction
- Equipment durability
- Loadout management

## Lessons Learned

### What Worked Well

- Separation of intents from execution
- Commitment units for reaction timing
- Deterministic simulation with seeds
- State-based validation

### What Could Improve

- Console input handling (non-interactive testing)
- More verbose logging options
- Better visualization of distance/positioning
- Tutorial/documentation for players

## Conclusion

The foundation successfully implements:
- ✅ Deterministic time-based simulation
- ✅ Raw weapon stats with no abstraction
- ✅ Tactical decision-making through reaction windows
- ✅ Clean separation of concerns
- ✅ Comprehensive test coverage (38 tests passing)

Ready for expansion into full game loop with campaign, multiple weapons, and advanced features.
