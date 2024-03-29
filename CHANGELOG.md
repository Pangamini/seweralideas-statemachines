# Changelog
## [0.2.3]
### Added
- StateMachine messages in Unity profiler
- Assert when the transition target state is not a part of the state machine
## [0.2.2]
### Added
- ConcurrentStateMachine wrapper for StateMachine
## [0.2.1]
### Removed
- StateMachine multi-threaded modes
  - StateMachines are no longer thread-safe!
  - Thread-safe stateMachine might be included as a wrapper later For now, you have to handle the thread-safety yourself, eg. by using locks.
## [0.2.0]
### Changed
- Hierarchical and Orthogonal states must now list their subStates during initialization.
## [0.1.2]
### Added
- State field values can now be displayed in StateMachineWindow
    - Options include all fields, or just those marked witb [ShowField] attribute

## [0.1.1]
### Fixed
- Bug related to exiting an orthogonal state