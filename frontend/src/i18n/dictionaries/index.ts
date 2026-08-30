// One file per screen/component (see the sibling files), merged here into a
// single { en, fr } tree keyed by namespace. Keeps each dictionary small and
// lets translation work be split across files with zero collisions — no two
// screens ever touch the same file.

import * as common from "./common";
import * as app from "./app";
import * as login from "./login";
import * as settings from "./settings";
import * as profileMenu from "./profileMenu";

export const dictionaries = {
  en: {
    common: common.en,
    app: app.en,
    login: login.en,
    settings: settings.en,
    profileMenu: profileMenu.en,
  },
  fr: {
    common: common.fr,
    app: app.fr,
    login: login.fr,
    settings: settings.fr,
    profileMenu: profileMenu.fr,
  },
};
