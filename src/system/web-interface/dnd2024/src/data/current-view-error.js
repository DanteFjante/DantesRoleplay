/** Internal authorization signal shared by the legacy Current adapter and its Redux owner. */
export class CurrentViewAuthorizationError extends Error {
  constructor(message = "The current scene is unavailable to this audience.") {
    super(message);
    this.name = "CurrentViewAuthorizationError";
  }
}
