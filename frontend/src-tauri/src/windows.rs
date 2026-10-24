// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

//! The windows this shell opens: the one the configuration describes, and on a desktop the one window the bundle may
//! open beside it.
//!
//! The main window is built here rather than by the configuration because only a builder can be told what to do when
//! the page asks for a window of its own. That page asks for exactly one — the developer information surface, which
//! it opens as `about:blank` and draws into from the main window — so that one is allowed and every other request is
//! refused: a link a reader follows leaves through the opener plugin, and nothing else in the bundle opens a window.

#[cfg(not(any(target_os = "android", target_os = "ios")))]
use std::sync::atomic::{AtomicU32, Ordering};

const MAIN_WINDOW: &str = "main";

#[cfg(not(any(target_os = "android", target_os = "ios")))]
const DEVELOPER_WINDOW_TITLE: &str = "MailFathom";

#[cfg(not(any(target_os = "android", target_os = "ios")))]
static OPENED_WINDOWS: AtomicU32 = AtomicU32::new(0);

/// Builds the main window from its configuration.
pub fn open_main(app: &tauri::App) -> tauri::Result<()> {
    let Some(configured) = app.config().app.windows.iter().find(|window| window.label == MAIN_WINDOW) else {
        return Ok(());
    };

    let builder = tauri::WebviewWindowBuilder::from_config(app, configured)?;

    #[cfg(not(any(target_os = "android", target_os = "ios")))]
    let builder = {
        let handle = app.handle().clone();

        builder.on_new_window(move |url, features| opened(&handle, url, features))
    };

    builder.build()?;

    Ok(())
}

/// Answers a page asking for a window: the developer information surface's blank document, and nothing else.
///
/// The window is related to the one that asked for it — `window_features` carries that on every desktop platform — which
/// is what lets the main window draw into it, so its document is the main window's own and it holds no capability.
#[cfg(not(any(target_os = "android", target_os = "ios")))]
fn opened(
    app: &tauri::AppHandle,
    url: tauri::Url,
    features: tauri::webview::NewWindowFeatures,
) -> tauri::webview::NewWindowResponse<tauri::Wry> {
    if url.as_str() != "about:blank" {
        return tauri::webview::NewWindowResponse::Deny;
    }

    let label = format!("developer-information-{}", OPENED_WINDOWS.fetch_add(1, Ordering::Relaxed));

    match tauri::WebviewWindowBuilder::new(app, label, tauri::WebviewUrl::External(url))
        .window_features(features)
        .title(DEVELOPER_WINDOW_TITLE)
        .on_document_title_changed(|window, title| {
            let _ = window.set_title(&title);
        })
        .build()
    {
        Ok(window) => tauri::webview::NewWindowResponse::Create { window },
        Err(_) => tauri::webview::NewWindowResponse::Deny,
    }
}
