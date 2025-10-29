import BaseController from "./BaseController";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";

/**
 * @namespace siagrob1.controller
 */
export default class Main extends BaseController {

	onInit(): void | undefined {
			const oData = new ODataModel({
				serviceUrl: "/odata/",
				synchronizationMode: "None",
				groupId: "$direct",
				operationMode: "Server",
				earlyRequests: true
			});
			this.getView().setModel(oData);
	}
}
