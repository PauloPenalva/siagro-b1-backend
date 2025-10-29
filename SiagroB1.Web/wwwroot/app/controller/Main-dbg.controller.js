sap.ui.define(["./BaseController", "sap/ui/model/odata/v4/ODataModel"], function (__BaseController, ODataModel) {
  "use strict";

  function _interopRequireDefault(obj) {
    return obj && obj.__esModule && typeof obj.default !== "undefined" ? obj.default : obj;
  }
  const BaseController = _interopRequireDefault(__BaseController);
  /**
   * @namespace siagrob1.controller
   */
  const Main = BaseController.extend("siagrob1.controller.Main", {
    onInit: function _onInit() {
      const oData = new ODataModel({
        serviceUrl: "/odata/",
        synchronizationMode: "None",
        groupId: "$direct",
        operationMode: "Server",
        earlyRequests: true
      });
      this.getView().setModel(oData);
    }
  });
  return Main;
});
//# sourceMappingURL=Main-dbg.controller.js.map
