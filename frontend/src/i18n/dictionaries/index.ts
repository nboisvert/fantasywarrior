// One file per screen/component (see the sibling files), merged here into a
// single { en, fr } tree keyed by namespace. Keeps each dictionary small and
// lets translation work be split across files with zero collisions — no two
// screens ever touch the same file.

import * as common from "./common";
import * as app from "./app";
import * as login from "./login";
import * as settings from "./settings";
import * as profileMenu from "./profileMenu";
import * as dashboard from "./dashboard";
import * as standings from "./standings";
import * as palmares from "./palmares";
import * as leagueGate from "./leagueGate";
import * as newsTicker from "./newsTicker";
import * as chatSheet from "./chatSheet";
import * as cockmanChat from "./cockmanChat";
import * as toast from "./toast";
import * as cockcoinReward from "./cockcoinReward";
import * as trades from "./trades";
import * as createTradeSheet from "./createTradeSheet";
import * as tradeVoteWidget from "./tradeVoteWidget";
import * as tradeRatingInfo from "./tradeRatingInfo";
import * as draftRoom from "./draftRoom";
import * as stats from "./stats";
import * as playerCard from "./playerCard";
import * as rulesPanel from "./rulesPanel";
import * as testModePanel from "./testModePanel";

export const dictionaries = {
  en: {
    common: common.en,
    app: app.en,
    login: login.en,
    settings: settings.en,
    profileMenu: profileMenu.en,
    dashboard: dashboard.en,
    standings: standings.en,
    palmares: palmares.en,
    leagueGate: leagueGate.en,
    newsTicker: newsTicker.en,
    chatSheet: chatSheet.en,
    cockmanChat: cockmanChat.en,
    toast: toast.en,
    cockcoinReward: cockcoinReward.en,
    trades: trades.en,
    createTradeSheet: createTradeSheet.en,
    tradeVoteWidget: tradeVoteWidget.en,
    tradeRatingInfo: tradeRatingInfo.en,
    draftRoom: draftRoom.en,
    stats: stats.en,
    playerCard: playerCard.en,
    rulesPanel: rulesPanel.en,
    testModePanel: testModePanel.en,
  },
  fr: {
    common: common.fr,
    app: app.fr,
    login: login.fr,
    settings: settings.fr,
    profileMenu: profileMenu.fr,
    dashboard: dashboard.fr,
    standings: standings.fr,
    palmares: palmares.fr,
    leagueGate: leagueGate.fr,
    newsTicker: newsTicker.fr,
    chatSheet: chatSheet.fr,
    cockmanChat: cockmanChat.fr,
    toast: toast.fr,
    cockcoinReward: cockcoinReward.fr,
    trades: trades.fr,
    createTradeSheet: createTradeSheet.fr,
    tradeVoteWidget: tradeVoteWidget.fr,
    tradeRatingInfo: tradeRatingInfo.fr,
    draftRoom: draftRoom.fr,
    stats: stats.fr,
    playerCard: playerCard.fr,
    rulesPanel: rulesPanel.fr,
    testModePanel: testModePanel.fr,
  },
};
