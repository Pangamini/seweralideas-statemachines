# Changelog
# [0.2.1]
### Removed
- StateMachine multithreaded modes
  - StateMachines are no longer thread-safe!
  - Thread-safe stateMachine might be incuded as a wrapper later For now, you have to handle the thread-safety yourself, eg. by using locks.
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