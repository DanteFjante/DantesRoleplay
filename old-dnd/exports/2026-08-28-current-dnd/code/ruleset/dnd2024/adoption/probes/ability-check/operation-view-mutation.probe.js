var role = ctx.roles.subject;
var before = role.components["operation-view"];
var assignmentRejected = false;
try {
  role.components["operation-view"] = "{}";
} catch (error) {
  assignmentRejected = true;
}
return {
  effects: [], events: [], notifications: [],
  data: {
    rolesFrozen: Object.isFrozen(ctx.roles),
    roleFrozen: Object.isFrozen(role),
    componentsFrozen: Object.isFrozen(role.components),
    assignmentRejected: assignmentRejected,
    valueUnchanged: role.components["operation-view"] === before
  }
};
