const state = {
  app: null,
  history: [],
  cellAudit: [],
  lotHistory: [],
  odooCandidates: [],
  odooSearchQuery: "",
  odooSearchStatus: "",
  odooLiveConfigured: false,
  odooSearchHandle: null,
  trace: [],
  intelligentRecipes: {},
  legacyRecipes: {},
  maintenance: null,
  activeTab: "production",
  maintenanceVisible: false,
  hiddenTapCount: 0,
  refreshHandle: null,
  auxHandle: null,
  commandInFlight: false,
  maintenanceCommandInFlight: false,
  pistonBusyLane: null,
  pistonCooldownUntil: 0
};

const PISTON_LANES = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "NG"];
const LEARNING_LANE_ID = "10";
const NG_LANE_ID = "NG";
const LEARNING_SAMPLE_TARGET = 19;
const QUALITY_INTERVAL_LANE_COUNT = 9;
const QUALITY_INTERVAL_GOOD_LANES = ["1", "2", "3", "4", "5", "6", "7", "8", "9", LEARNING_LANE_ID];
const PRESERVED_SCROLL_SELECTORS = [
  "#qualityIntervalChart .interval-scroll",
  "#laneSummary .lane-fill-list",
  "#sampleGrid .sample-scroll",
  "#historyTable .history-table",
  "#lotHistory .lot-list",
  "#traceTable .trace-table"
];

function escapeHtml(value) {
  return String(value == null ? "" : value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

async function apiGet(path) {
  const response = await fetch(path, { cache: "no-store" });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({ error: response.statusText }));
    throw new Error(payload.error || response.statusText);
  }
  return response.json();
}

async function apiPost(path, payload) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload || {})
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({ error: response.statusText }));
    throw new Error(body.error || response.statusText);
  }
  return response.json();
}

function setNotification(message, tone = "info", sticky = false) {
  const node = document.getElementById("notification");
  if (!message) {
    node.className = "notification hidden";
    node.textContent = "";
    return;
  }

  node.className = `notification ${tone}`;
  node.textContent = message;

  if (!sticky) {
    window.clearTimeout(setNotification._timer);
    setNotification._timer = window.setTimeout(() => {
      if (node.textContent === message) {
        setNotification(null);
      }
    }, 4200);
  }
}

function formatNumber(value, digits = 3) {
  const num = Number(value || 0);
  return Number.isFinite(num) ? num.toFixed(digits) : "--";
}

function formatInt(value) {
  const num = Number(value || 0);
  return Number.isFinite(num) ? String(Math.trunc(num)) : "--";
}

function setText(id, value) {
  const node = document.getElementById(id);
  if (node) {
    node.textContent = value == null ? "" : String(value);
  }
}

function setHtml(id, value) {
  const node = document.getElementById(id);
  if (node) {
    node.innerHTML = value == null ? "" : String(value);
  }
}

function captureScrollState() {
  return {
    windowX: window.scrollX || 0,
    windowY: window.scrollY || 0,
    containers: PRESERVED_SCROLL_SELECTORS.map((selector) => {
      const node = document.querySelector(selector);
      return node
        ? { selector, left: node.scrollLeft || 0, top: node.scrollTop || 0 }
        : null;
    }).filter(Boolean)
  };
}

function restoreScrollState(snapshot) {
  if (!snapshot) {
    return;
  }

  snapshot.containers.forEach((item) => {
    const node = document.querySelector(item.selector);
    if (node) {
      node.scrollLeft = item.left;
      node.scrollTop = item.top;
    }
  });

  window.scrollTo(snapshot.windowX || 0, snapshot.windowY || 0);
}

function badge(label, kind = "neutral") {
  return `<span class="badge is-${kind}">${escapeHtml(label)}</span>`;
}

function renderRecipes() {
  const app = state.app;
  if (!app) {
    return;
  }

  const cellType = app.Config.CellType;
  const intelligent = state.intelligentRecipes[cellType];
  const legacy = state.legacyRecipes[cellType];

  document.getElementById("cellTypeSelect").value = cellType;
  document.getElementById("sortingModeSelect").value = app.Config.SortingMode;

  if (intelligent) {
    document.getElementById("intelligentRecipeForm").innerHTML = `
      <div class="note">
        <strong>Règle active du lot</strong>
        <div class="muted">Ligne 10 = apprentissage ${LEARNING_SAMPLE_TARGET} cellules. Après gel, lignes 1 à ${QUALITY_INTERVAL_LANE_COUNT} = intervalles résistance figés; la tension sert uniquement de garde sous-charge / surcharge. La machine gère le plein physique.</div>
      </div>
      <div class="form-grid">
        <label><span>Échantillon ligne 10 (fixe)</span><input id="intSampleSize" type="number" min="${LEARNING_SAMPLE_TARGET}" max="${LEARNING_SAMPLE_TARGET}" readonly value="${LEARNING_SAMPLE_TARGET}"></label>
        <label><span>Ligne apprentissage</span><input id="intLastGoodLane" type="text" readonly value="${LEARNING_LANE_ID}"></label>
        <label><span>Sigma max tension</span><input id="intMaxSigmaVoltage" type="number" step="0.001" value="${escapeHtml(intelligent.MaxSigmaVoltage)}"></label>
        <label><span>Sigma max IR</span><input id="intMaxSigmaIr" type="number" step="0.001" value="${escapeHtml(intelligent.MaxSigmaIr)}"></label>
        <label><span>K tension</span><input id="intAcceptanceKVoltage" type="number" step="0.1" value="${escapeHtml(intelligent.AcceptanceKVoltage)}"></label>
        <label><span>K IR</span><input id="intAcceptanceKIr" type="number" step="0.1" value="${escapeHtml(intelligent.AcceptanceKIr)}"></label>
        <label><span>Fenêtre min tension</span><input id="intMinWindowVoltage" type="number" step="0.001" value="${escapeHtml(intelligent.MinWindowVoltage)}"></label>
        <label><span>Fenêtre max tension</span><input id="intMaxWindowVoltage" type="number" step="0.001" value="${escapeHtml(intelligent.MaxWindowVoltage)}"></label>
        <label><span>Fenêtre min IR</span><input id="intMinWindowIr" type="number" step="0.001" value="${escapeHtml(intelligent.MinWindowIr)}"></label>
        <label><span>Fenêtre max IR</span><input id="intMaxWindowIr" type="number" step="0.001" value="${escapeHtml(intelligent.MaxWindowIr)}"></label>
        <label><span>Lignes tri + apprentissage</span><input id="intGoodLanes" type="text" readonly value="${escapeHtml(QUALITY_INTERVAL_GOOD_LANES.join(", "))}"></label>
        <label><span>Voie NG</span><input id="intNgLane" type="text" readonly value="${NG_LANE_ID}"></label>
        <label><span>Timeout apprentissage</span><input id="intLearningTimeout" type="number" min="1" value="${escapeHtml(intelligent.LearningTimeoutCells)}"></label>
      </div>
      <input id="intPreSwitchMargin" type="hidden" value="${escapeHtml(intelligent.LanePreSwitchMargin || 0)}">
    `;
  }

  if (legacy) {
    const rows = (legacy.Channels || []).map((channel, index) => `
      <div class="threshold-row">
        <div class="threshold-grid">
          <strong>${index + 1}</strong>
          <input type="number" step="0.0001" data-th="${index}:vmin" value="${escapeHtml(channel.VoltageMin)}">
          <input type="number" step="0.0001" data-th="${index}:vmax" value="${escapeHtml(channel.VoltageMax)}">
          <input type="number" step="0.0001" data-th="${index}:irmin" value="${escapeHtml(channel.IrMin)}">
          <input type="number" step="0.0001" data-th="${index}:irmax" value="${escapeHtml(channel.IrMax)}">
        </div>
      </div>`).join("");

    document.getElementById("legacyRecipeForm").innerHTML = `
      <div class="form-grid">
        <label>
          <span>Mode legacy</span>
          <select id="legacyJudgeMode">
            <option value="BOTH" ${app.Config.JudgeMode === "BOTH" ? "selected" : ""}>Voltage + IR</option>
            <option value="VOLTAGE" ${app.Config.JudgeMode === "VOLTAGE" ? "selected" : ""}>Voltage</option>
            <option value="IR" ${app.Config.JudgeMode === "IR" ? "selected" : ""}>IR</option>
          </select>
        </label>
        <label><span>Premier canal</span><input id="legacyChannelStart" type="number" min="1" value="${escapeHtml(app.Config.ChannelStart)}"></label>
        <label><span>Dernier canal</span><input id="legacyChannelEnd" type="number" min="1" value="${escapeHtml(app.Config.ChannelEnd)}"></label>
      </div>
      <div class="threshold-table">${rows}</div>
    `;
  }
}

function renderHistory() {
  const rows = (state.cellAudit || []).slice().reverse();
  document.getElementById("historyTable").innerHTML = rows.length
    ? `<div class="history-table">${rows.map((row) => `
      <div class="history-row">
        <header>
          <strong>#${formatInt(row.Sequence)} · ${escapeHtml(row.Result || row.Decision || "--")} · appliquée ${escapeHtml(row.EffectiveLane || "--")}</strong>
          ${badge(row.Mismatch ? "Mismatch" : row.DataQuality === "CONFIRMED" ? "Confirmée" : "En attente", row.Mismatch ? "danger" : row.DataQuality === "CONFIRMED" ? "live" : "pause")}
        </header>
        <small>${escapeHtml(row.Timestamp || "")} · ${escapeHtml(row.CellType || "")} · lot ${formatInt(row.LotId)} · HS ${escapeHtml(row.Handshake ?? "--")}</small>
        <div class="audit-grid">
          <span>Décidée <strong>${escapeHtml(row.IntendedLane || "--")}</strong></span>
          <span>Appliquée <strong>${escapeHtml(row.EffectiveLane || "--")}</strong></span>
          <span>Confirmée <strong>${escapeHtml(row.ConfirmationLane || "—")}</strong></span>
          <span>Modèle <strong>${escapeHtml(row.RoutingModel || "--")}</strong></span>
          <span>Intervalle <strong>${escapeHtml(row.QualityInterval ?? "--")}</strong></span>
          <span>V <strong>${formatNumber(row.Voltage, 4)}</strong></span>
          <span>IR <strong>${formatNumber(row.Ir, 3)}</strong></span>
          <span>Seuil V <strong>${escapeHtml(formatRange(row.VoltageMin, row.VoltageMax, 4, "V"))}</strong></span>
          <span>Seuil IR <strong>${escapeHtml(formatRange(row.IrMin, row.IrMax, 3, "mΩ"))}</strong></span>
          <span>Motif <strong>${escapeHtml(row.RejectReason || "—")}</strong></span>
        </div>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Audit vide</strong><div class="muted">Les cellules apparaîtront ici avec décision, routage appliqué et confirmation physique.</div></div>`;

  const lots = state.lotHistory || [];
  document.getElementById("lotHistory").innerHTML = lots.length
    ? `<div class="lot-list">${lots.map((lot) => `
      <div class="lot-row">
        <header>
          <strong>Lot ${formatInt(lot.Id)}</strong>
          ${badge(lot.IsActive ? "Actif" : "Clos", lot.IsActive ? "live" : "neutral")}
        </header>
        <small>${escapeHtml(lot.StartedAt || "")}${lot.ClosedAt ? ` → ${escapeHtml(lot.ClosedAt)}` : ""}</small>
        <div class="history-meta">${escapeHtml(lot.SortingMode || "")} • ${escapeHtml(lot.CellType || "")} • GOOD ${formatInt(lot.GoodCount)} / NG ${formatInt(lot.NgCount)}</div>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Pas d’historique de lot</strong><div class="muted">Les nouveaux lots apparaîtront ici.</div></div>`;
}

function renderDiagnostic() {
  const app = state.app;
  if (!app) {
    return;
  }

  const diag = app.Diagnostic || {};
  const notes = diag.Notes || [];
  const diffs = diag.ThresholdDifferences || [];
  const rows = state.trace || [];
  const routing = diag.PhysicalRouting || {};
  const readiness = diag.StartReadiness || {};
  const fieldValidation = diag.FieldValidation || {};
  const lastNgPulse = routing.LastNgPulse || {};
  const programmedCount = routing.ProgrammedThresholds?.Channels?.length || 0;
  const observedCount = routing.ObservedThresholds?.Channels?.length || 0;
  const readinessProblems = readiness.BlockingReasons || [];
  const readinessWarnings = readiness.Warnings || [];
  const fieldValidationCard = `<div class="note"><strong>Validation terrain</strong><div class="muted">${fieldValidation.Verified ? "Rapport terrain valide" : "Preuve terrain non validee"} &middot; ${escapeHtml(fieldValidation.Status || "NO_REPORT")} &middot; ${escapeHtml(fieldValidation.ReportTimestamp || "--")}</div><div class="muted">Lot rapport ${escapeHtml(fieldValidation.ReportLotId ?? "--")} &middot; lot courant ${escapeHtml(fieldValidation.CurrentLotId ?? "--")} &middot; ${fieldValidation.MatchesCurrentLot ? "lot conforme" : "lot non confirme"}</div><div class="muted">Trace ${escapeHtml(fieldValidation.TraceVerdict || "UNKNOWN")} &middot; compteurs ${escapeHtml(fieldValidation.CounterVerdict || "UNKNOWN")} &middot; observation ${escapeHtml(fieldValidation.PhysicalObservationVerdict || "UNKNOWN")} &middot; voies ${escapeHtml(fieldValidation.LaneCoverageVerdict || "UNKNOWN")}</div><div class="muted">${escapeHtml(fieldValidation.Summary || "Lancer le controle terrain avant l'essai physique.")}</div><div class="muted">Commandes: ${escapeHtml(fieldValidation.ValidationCommand || "validate_tricell_field.bat 180")} puis refresh_tricell_field_result.bat si CSV complete apres coup, puis ${escapeHtml(fieldValidation.CheckCommand || "check_tricell_field_result.bat")}</div></div>`;

  document.getElementById("diagnosticSummary").innerHTML = `
    <div class="stack">
      ${fieldValidationCard}
      <div class="note"><strong>Handshake</strong><div class="muted">Registre ${formatInt(diag.HandshakeRegister)} • valeur ${escapeHtml(diag.HandshakeValue ?? "--")}</div></div>
      <div class="note"><strong>Status</strong><div class="muted">Registre ${formatInt(diag.StatusRegister)} • valeur ${escapeHtml(diag.StatusValue ?? "--")}</div></div>
      <div class="note"><strong>Pré-vol DÉMARRER</strong><div class="muted">${readiness.ReadyToStart ? "Prêt à démarrer avec opérateur présent" : "À contrôler avant démarrage"} &middot; statut ${escapeHtml(readiness.MachineStatus ?? "--")} &middot; lot ${escapeHtml(readiness.LotStatus || "--")} &middot; 8230 ${readiness.HandshakeReady ? "lu" : "non lu"} ${escapeHtml(readiness.HandshakeValue ?? "--")}</div><div class="muted">${readinessProblems.length ? escapeHtml(readinessProblems.join(" | ")) : "Aucun blocage logiciel détecté."}</div>${readinessWarnings.length ? `<div class="muted">${escapeHtml(readinessWarnings.join(" | "))}</div>` : ""}</div>
      <div class="note"><strong>Routage physique</strong><div class="muted">Mode ${escapeHtml(routing.PhysicalRoutingMode || "PLC_THRESHOLDS_NG_CATCHALL")} &middot; attendue ${escapeHtml(routing.ExpectedLane || "--")} &middot; appliquee ${escapeHtml(routing.AppliedLane || "--")} &middot; confirmee ${escapeHtml(routing.ConfirmedLane || "--")}</div><div class="muted">Seuils programmes ${formatInt(programmedCount)} &middot; seuils relus ${formatInt(observedCount)} &middot; ${escapeHtml(routing.ThresholdStatus || diag.ThresholdStatus || "--")}</div></div>
      <div class="note"><strong>NG production</strong><div class="muted">Vérin NG poussé par le PLC via la voie 11 catch-all à chaque cellule non triée GOOD.</div><div class="muted">Dernier pulse Y11 maintenance: ${escapeHtml(lastNgPulse.Status || "NONE")} &middot; ${escapeHtml(lastNgPulse.Timestamp || "--")}</div></div>
      <div class="note"><strong>Scanner</strong><div class="muted">${escapeHtml(diag.ScannerStatus || "—")} • parité ${escapeHtml(diag.ScannerParity || "—")}</div></div>
      <div class="note"><strong>Compteurs</strong><div class="muted">Source ${escapeHtml(app.Counters?.Source || "--")} • total ${formatInt(app.Counters?.Total)}</div></div>
      <div class="note"><strong>Mesures brutes</strong><div class="muted">${escapeHtml((diag.MeasurementRegisters || []).join(" / "))}</div></div>
    </div>
  `;

  document.getElementById("diagnosticNotes").innerHTML = notes.length
    ? `<div class="stack">${notes.map((note) => `<div class="note">${escapeHtml(note)}</div>`).join("")}</div>`
    : `<div class="note"><strong>Aucune note</strong><div class="muted">Le diagnostic n’a rien ajouté de particulier.</div></div>`;

  document.getElementById("thresholdDiffs").innerHTML = diffs.length
    ? `<div class="diff-list">${diffs.slice(0, 80).map((diff) => `
      <div class="diff-row">
        <strong>Canal ${formatInt(diff.Channel)} • ${escapeHtml(diff.Field)}</strong>
        <small>Local ${formatNumber(diff.LocalValue, 4)}</small>
        <small>Machine ${formatNumber(diff.ObservedValue, 4)}</small>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Aucun écart remonté</strong><div class="muted">Les seuils lus sur la machine restent cohérents avec la configuration locale.</div></div>`;

  document.getElementById("runtimeTrace").innerHTML = rows.length
    ? `<div class="trace-table">${rows.map((row) => `
      <div class="trace-row">
        <header>
          <strong>${escapeHtml(row.Category)} • ${escapeHtml(row.Action)}</strong>
          ${badge(row.Status || "--", row.Status && row.Status.includes("ERROR") ? "danger" : row.Status && row.Status.includes("ATTEMPT") ? "pause" : "neutral")}
        </header>
        <small>${escapeHtml(row.Timestamp || "")} • ${escapeHtml(row.Source || "")} • reg ${escapeHtml(row.Register || "--")} • val ${escapeHtml(row.Value || "--")}</small>
        <div class="trace-meta">${escapeHtml(row.Detail || "")}</div>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Trace vide</strong><div class="muted">Les commandes et décisions apparaîtront ici.</div></div>`;
}

function renderMaintenance() {
  const maintenance = state.maintenance;
  if (!maintenance) {
    return;
  }

  const validated = maintenance.ValidatedCommands || [];
  document.getElementById("validatedMaintenance").innerHTML = validated.length
    ? `<div class="stack">${validated.map((command) => `
      <div class="note">
        <header>
          <strong>${escapeHtml(command.Label)}</strong>
          ${badge(command.TerrainValidated ? "Terrain" : "Sécurisé", command.TerrainValidated ? "live" : "pause")}
        </header>
        <div class="history-meta">Registre ${escapeHtml(command.Register)} • code ${escapeHtml(command.Code)}</div>
        ${command.Warning ? `<div class="history-meta">${escapeHtml(command.Warning)}</div>` : ""}
        <div class="button-row">
          <button class="primary-button" type="button" data-maint-command="${escapeHtml(command.Command)}">Envoyer</button>
        </div>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Aucune commande validée</strong></div>`;

  document.getElementById("expertSessionBanner").innerHTML =
    "";

  const expert = maintenance.ExpertCommands || [];
  document.getElementById("expertMaintenance").innerHTML = expert.length
    ? `<div class="stack">${expert.map((command) => `
      <div class="note">
        <header>
          <strong>${escapeHtml(command.Label)}</strong>
          ${badge("Non validé terrain", "danger")}
        </header>
        <div class="history-meta">Registre ${escapeHtml(command.Register)} • code ${escapeHtml(command.Code)}</div>
        <div class="history-meta">${escapeHtml(command.Warning || "")}</div>
        <div class="button-row">
          <button class="ghost-button" type="button" data-maint-command="${escapeHtml(command.Command)}">Envoyer</button>
        </div>
      </div>`).join("")}</div>`
    : `<div class="note"><strong>Aucun essai non validé</strong></div>`;

  renderPistonTester(maintenance);
}

function getPistonIoMap(lane) {
  const physicalLane = lane === "NG" ? 11 : Number(lane);
  const index = physicalLane - 1;
  return {
    lane,
    physicalLane,
    reset: 28295 + index,
    enable: 28414 + index,
    output: 28926 + index,
    readback: 28679 + index,
    state: 28158 + index
  };
}

function renderPistonTester() {
  const node = document.getElementById("pistonTester");
  if (!node) {
    return;
  }

  node.innerHTML = `
    <div class="piston-test-head">
      <div class="note">
        <strong>Diagnostic I/O pistons</strong>
        <div class="muted">Test manuel un par un, machine arrêtée. Les resets ne sont pas pulsés.</div>
      </div>
    </div>
    <div class="piston-grid">
      ${PISTON_LANES.map((lane) => {
        const map = getPistonIoMap(lane);
        const isNg = lane === "NG";
        return `
        <article class="piston-card">
          <strong>${lane === "NG" ? "NG" : `Ligne ${lane}`}</strong>
          <div class="piston-io-map">
            <span>Reset <code>${escapeHtml(map.reset)}</code> non pulsé en test direct</span>
            <span>Enable <code>${escapeHtml(map.enable)}</code> + sortie <code>${escapeHtml(map.output)}</code></span>
            <span>Retour <code>${escapeHtml(map.readback)}</code> / état <code>${escapeHtml(map.state)}</code></span>
          </div>
          <div class="button-row">
            <button class="ghost-button" type="button" data-piston-prepare="${escapeHtml(lane)}">Tester</button>
            ${isNg ? `<button class="ghost-button" type="button" data-maint-command="DIAG_PULSE_NG">Diag sortie Y11 (carte)</button>` : ""}
          </div>
        </article>`;
      }).join("")}
    </div>
    <div class="muted piston-help">Les tests sont bloqués automatiquement si le cycle est armé ou si une sécurité machine active empêche les vérins.</div>
    <div id="pistonLastStatus" class="muted piston-help"></div>
  `;
}

function isPistonCooldownActive() {
  return Date.now() < (state.pistonCooldownUntil || 0);
}

function applyInteractionLocks() {
  const commandBusy = !!state.commandInFlight;
  document.querySelectorAll("[data-command]").forEach((button) => {
    const logicDisabled = button.dataset.logicDisabled === "true";
    const disabled = commandBusy || logicDisabled;
    button.disabled = disabled;
    button.classList.toggle("is-disabled", disabled);
  });

  const maintenanceBusy = !!state.maintenanceCommandInFlight;
  document.querySelectorAll("[data-maint-command]").forEach((button) => {
    const disabled = maintenanceBusy || !!state.pistonBusyLane;
    button.disabled = disabled;
    button.classList.toggle("is-disabled", disabled);
  });

  const pistonLocked = !!state.pistonBusyLane || isPistonCooldownActive() || maintenanceBusy;
  document.querySelectorAll("[data-piston-prepare]").forEach((button) => {
    const lane = (button.dataset.pistonPrepare || "").toUpperCase();
    const activeLane = (state.pistonBusyLane || "").toUpperCase();
    const disabledBySafety = button.dataset.pistonDisabled === "true";
    const disabled = disabledBySafety || pistonLocked;
    button.disabled = disabled;
    button.classList.toggle("is-disabled", disabled);
    button.textContent = disabledBySafety ? "Bloqué" : (lane && lane === activeLane ? "Envoi..." : "Tester");
  });
}

function bindGlobalEvents() {
  document.querySelectorAll(".chip[data-tab]").forEach((button) => {
    button.addEventListener("click", () => {
      state.activeTab = button.dataset.tab;
      render({ preserveScroll: false });
      window.scrollTo(0, 0);
    });
  });

  document.getElementById("refreshButton").addEventListener("click", () => window.location.reload());
  document.getElementById("maintenanceShortcutButton").addEventListener("click", () => {
    state.maintenanceVisible = true;
    state.activeTab = "maintenance";
    render({ preserveScroll: false });
    window.scrollTo(0, 0);
  });
  const odooForm = document.getElementById("odooLinkForm");
  if (odooForm) {
    odooForm.addEventListener("submit", linkOdooLot);
  }
  const odooSearchInput = document.getElementById("odooLotSearchInput");
  if (odooSearchInput) {
    odooSearchInput.addEventListener("input", (event) => scheduleOdooSearch(event.target.value));
    odooSearchInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        searchOdooLots(odooSearchInput.value);
      }
    });
  }
  const odooSearchButton = document.getElementById("odooLotSearchButton");
  if (odooSearchButton) {
    odooSearchButton.addEventListener("click", () => searchOdooLots(odooSearchInput ? odooSearchInput.value : ""));
  }

  const newLotButton = document.getElementById("newLotButton");
  if (newLotButton) newLotButton.addEventListener("click", () => runLotAction("new"));
  const continueLotButton = document.getElementById("continueLotButton");
  if (continueLotButton) continueLotButton.addEventListener("click", () => runLotAction("continue"));
  const closeLotButton = document.getElementById("closeLotButton");
  if (closeLotButton) closeLotButton.addEventListener("click", () => runLotAction("close"));
  const resetLinesButton = document.getElementById("resetLinesButton");
  if (resetLinesButton) resetLinesButton.addEventListener("click", () => runLotAction("reset-lines"));
  const resetLinesPilotButton = document.getElementById("resetLinesPilotButton");
  if (resetLinesPilotButton) resetLinesPilotButton.addEventListener("click", () => runLotAction("reset-lines"));
  document.getElementById("cellTypeSelect").addEventListener("change", async (event) => {
    try {
      await apiPost("/api/config/cell-type", { cell_type: event.target.value });
      setNotification(`Type cellule actif : ${event.target.value}`, "success");
      await refreshAll(true);
    } catch (error) {
      setNotification(error.message, "error");
    }
  });

  document.getElementById("sortingModeSelect").addEventListener("change", async (event) => {
    try {
      await apiPost("/api/config/sorting-mode", { sorting_mode: event.target.value });
      setNotification(`Mode actif : ${event.target.value}`, "success");
      await refreshAll(true);
    } catch (error) {
      setNotification(error.message, "error");
    }
  });

  document.getElementById("saveIntelligentRecipeButton").addEventListener("click", saveIntelligentRecipe);
  document.getElementById("saveLegacyRecipeButton").addEventListener("click", saveLegacyRecipe);

  document.querySelectorAll("[data-command]").forEach((button) => {
    button.addEventListener("click", async () => {
      if (state.commandInFlight) {
        return;
      }

      const command = (button.dataset.command || "").toUpperCase();
      try {
        state.commandInFlight = true;
        applyInteractionLocks();
        const payload = await apiPost("/api/command", { command });
        setNotification(payload.Message || `${command} envoyé`, "success");
        await refreshAll(true);
      } catch (error) {
        setNotification(error.message, "error");
        await refreshAll(true);
      } finally {
        state.commandInFlight = false;
        applyInteractionLocks();
      }
    });
  });

  document.body.addEventListener("click", (event) => {
    const odooSuggestion = event.target.closest("[data-odoo-suggestion]");
    if (odooSuggestion) {
      setInputValue("odooLotReferenceInput", odooSuggestion.dataset.odooRef || "");
      setInputValue("odooLotNameInput", odooSuggestion.dataset.odooName || "");
      setInputValue("odooProductInput", odooSuggestion.dataset.odooProduct || "");
      associateOdooSuggestion(odooSuggestion);
      return;
    }

    const validatedButton = event.target.closest("[data-maint-command]");
    if (validatedButton) {
      sendMaintenanceCommand(validatedButton.dataset.maintCommand);
      return;
    }

    const pistonButton = event.target.closest("[data-piston-prepare]");
    if (pistonButton) {
      sendPistonTest(pistonButton.dataset.pistonPrepare, pistonButton);
    }
  });

  document.body.addEventListener("change", (event) => {
    const select = event.target.closest("[data-odoo-select]");
    if (!select) {
      return;
    }

    const index = Number(select.value);
    const lot = Number.isInteger(index) ? state.odooCandidates[index] : null;
    if (!lot) {
      return;
    }

    setInputValue("odooLotReferenceInput", lot.Reference || "");
    setInputValue("odooLotNameInput", lot.Name || "");
    setInputValue("odooProductInput", lot.ProductReference || lot.ProductName || "");
    associateOdooCandidateData(lot, select);
  });

  document.getElementById("brandButton").addEventListener("click", () => {
    state.hiddenTapCount += 1;
    if (state.hiddenTapCount >= 5) {
      state.maintenanceVisible = true;
      state.activeTab = "maintenance";
      setNotification("Maintenance visible pour cette session.", "warning");
      render({ preserveScroll: false });
      window.scrollTo(0, 0);
      state.hiddenTapCount = 0;
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "m") {
      state.maintenanceVisible = !state.maintenanceVisible;
      if (state.maintenanceVisible) {
        state.activeTab = "maintenance";
        setNotification("Maintenance affichée.", "warning");
      } else if (state.activeTab === "maintenance") {
        state.activeTab = "production";
        setNotification("Maintenance masquée.", "info");
      }
      render({ preserveScroll: false });
      window.scrollTo(0, 0);
    }
  });
}

async function linkOdooLot(event) {
  if (event) {
    event.preventDefault();
  }
  try {
    const searchedLot = (document.getElementById("odooLotSearchInput")?.value || "").trim();
    const typedReference = (document.getElementById("odooLotReferenceInput").value || "").trim();
    const typedName = (document.getElementById("odooLotNameInput").value || "").trim();
    const typedProduct = (document.getElementById("odooProductInput").value || "").trim();
    const payload = {
      odoo_lot_reference: typedReference || searchedLot,
      odoo_lot_name: typedName,
      odoo_product_reference: typedProduct,
      odoo_product_name: typedProduct,
      note: ""
    };
    const result = await apiPost("/api/lots/odoo-link", payload);
    setNotification(result.Message || "Lot de cellules Odoo associé.", "success");
    await searchOdooLots("", true);
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error");
  }
}

async function associateOdooSuggestion(button) {
  try {
    if (!button) {
      return;
    }

    button.disabled = true;
    await associateOdooCandidateData({
      Reference: button.dataset.odooRef || "",
      Name: button.dataset.odooName || "",
      ProductReference: button.dataset.odooProduct || "",
      ProductName: button.dataset.odooProduct || ""
    }, button);
  } finally {
    if (button) {
      button.disabled = false;
    }
  }
}

async function associateOdooCandidateData(lot, control) {
  try {
    if (!lot) {
      return;
    }

    if (control) {
      control.disabled = true;
    }

    const product = lot.ProductReference || lot.ProductName || "";
    const payload = {
      odoo_lot_reference: (lot.Reference || "").trim(),
      odoo_lot_name: (lot.Name || "").trim(),
      odoo_product_reference: product.trim(),
      odoo_product_name: product.trim(),
      note: ""
    };

    const result = await apiPost("/api/lots/odoo-link", payload);
    setNotification(result.Message || "Lot de cellules Odoo associé.", "success");
    await searchOdooLots("", true);
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error");
  } finally {
    if (control) {
      control.disabled = false;
    }
  }
}

async function sendMaintenanceCommand(command) {
  if (state.maintenanceCommandInFlight) {
    return;
  }

  try {
    state.maintenanceCommandInFlight = true;
    applyInteractionLocks();
    const result = await apiPost("/api/maintenance/command", { command });
    setNotification(result.Message || `${command} envoyé`, result.TerrainValidated ? "success" : "warning", !result.Ok);
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error", true);
  } finally {
    state.maintenanceCommandInFlight = false;
    applyInteractionLocks();
  }
}

function readPistonPayload(lane) {
  if (!PISTON_LANES.includes(lane)) {
    setNotification("Ligne piston invalide.", "error", true);
    return;
  }

  return { lane };
}

async function sendPistonTest(lane, button) {
  if (state.pistonBusyLane || isPistonCooldownActive()) {
    return;
  }

  const payload = readPistonPayload(lane);
  if (!payload) {
    return;
  }

  try {
    state.pistonBusyLane = payload.lane;
    applyInteractionLocks();
    const result = await apiPost("/api/maintenance/piston-test", payload);
    await refreshAll(true);
    const map = getPistonIoMap(lane);
    const stateParts = [
      result.StateBefore != null ? `avant ${result.StateBefore}` : null,
      result.StateDuring != null ? `pendant ${result.StateDuring}` : null,
      result.StateAfter != null ? `après ${result.StateAfter}` : null
    ].filter(Boolean).join(", ");
    const ioSummary = `Reset ${map.reset}, enable ${map.enable}, sortie ${map.output}, retour ${map.readback}`;
    setPistonStatus(`Dernier essai ${lane === "NG" ? "NG" : `ligne ${lane}`}: ${result.Message || "test direct terminé"}. ${ioSummary}${stateParts ? ` (${stateParts})` : ""}.`);
  } catch (error) {
    setNotification(error.message, "error", true);
    setPistonStatus(`Dernier essai: erreur - ${error.message}`);
  } finally {
    state.pistonBusyLane = null;
    state.pistonCooldownUntil = Date.now() + 1200;
    applyInteractionLocks();
    window.setTimeout(() => {
      if (!isPistonCooldownActive()) {
        applyInteractionLocks();
      }
    }, 1250);
  }
}

function setPistonStatus(message) {
  const node = document.getElementById("pistonLastStatus");
  if (node) {
    node.textContent = message || "";
  }
}

async function saveIntelligentRecipe() {
  const app = state.app;
  if (!app) return;
  const cellType = app.Config.CellType;
  const recipe = JSON.parse(JSON.stringify(state.intelligentRecipes[cellType]));
  recipe.SampleSize = LEARNING_SAMPLE_TARGET;
  recipe.LastGoodLane = LEARNING_LANE_ID;
  recipe.MaxSigmaVoltage = Number(document.getElementById("intMaxSigmaVoltage").value || 0);
  recipe.MaxSigmaIr = Number(document.getElementById("intMaxSigmaIr").value || 0);
  recipe.AcceptanceKVoltage = Number(document.getElementById("intAcceptanceKVoltage").value || 0);
  recipe.AcceptanceKIr = Number(document.getElementById("intAcceptanceKIr").value || 0);
  recipe.MinWindowVoltage = Number(document.getElementById("intMinWindowVoltage").value || 0);
  recipe.MaxWindowVoltage = Number(document.getElementById("intMaxWindowVoltage").value || 0);
  recipe.MinWindowIr = Number(document.getElementById("intMinWindowIr").value || 0);
  recipe.MaxWindowIr = Number(document.getElementById("intMaxWindowIr").value || 0);
  recipe.GoodLanes = QUALITY_INTERVAL_GOOD_LANES.slice();
  recipe.NgLane = NG_LANE_ID;
  recipe.LanePreSwitchMargin = Number(document.getElementById("intPreSwitchMargin").value || 0);
  recipe.LearningTimeoutCells = Number(document.getElementById("intLearningTimeout").value || 0);
  recipe.LaneCapacities = recipe.LaneCapacities || [];

  try {
    await apiPost(`/api/recipes/intelligent/${cellType}`, recipe);
    setNotification(`Recette intelligente ${cellType} enregistrée.`, "success");
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error");
  }
}

async function saveLegacyRecipe() {
  const app = state.app;
  if (!app) return;
  const cellType = app.Config.CellType;
  const recipe = JSON.parse(JSON.stringify(state.legacyRecipes[cellType]));
  recipe.Channels = (recipe.Channels || []).map((channel, index) => ({
    VoltageMin: Number(document.querySelector(`[data-th="${index}:vmin"]`)?.value || channel.VoltageMin || 0),
    VoltageMax: Number(document.querySelector(`[data-th="${index}:vmax"]`)?.value || channel.VoltageMax || 0),
    IrMin: Number(document.querySelector(`[data-th="${index}:irmin"]`)?.value || channel.IrMin || 0),
    IrMax: Number(document.querySelector(`[data-th="${index}:irmax"]`)?.value || channel.IrMax || 0)
  }));

  try {
    await apiPost(`/api/recipes/legacy/${cellType}`, recipe);
    await apiPost("/api/config/legacy-options", {
      judge_mode: document.getElementById("legacyJudgeMode").value,
      channel_start: Number(document.getElementById("legacyChannelStart").value || 1),
      channel_end: Number(document.getElementById("legacyChannelEnd").value || 1)
    });
    await apiPost("/api/config/sorting-mode", { sorting_mode: document.getElementById("sortingModeSelect").value });
    setNotification(`Recette legacy ${cellType} enregistrée.`, "success");
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error");
  }
}

async function refreshAll(forceAux = false) {
  try {
    state.app = await apiGet("/api/state");
    renderTopbar();
    render();

    if (forceAux) {
      await refreshAuxiliary();
    }
  } catch (error) {
    setNotification(error.message, "error", true);
  }
}

async function refreshAuxiliary() {
  const app = state.app;
  if (!app) {
    return;
  }

  const cellType = app.Config.CellType;
  try {
    const [history, audit, lots, trace, maintenance, intelligent, legacy] = await Promise.all([
      apiGet("/api/history?limit=80"),
      apiGet("/api/cells/audit?limit=500"),
      apiGet("/api/lots/history?limit=20"),
      apiGet("/api/runtime-trace?limit=120"),
      apiGet("/api/maintenance"),
      apiGet(`/api/recipes/intelligent/${cellType}`),
      apiGet(`/api/recipes/legacy/${cellType}`)
    ]);

    state.history = history.cells || [];
    state.cellAudit = audit.cells || [];
    state.lotHistory = lots.lots || [];
    state.trace = trace.rows || [];
    state.maintenance = maintenance.maintenance || null;
    state.intelligentRecipes[cellType] = intelligent;
    state.legacyRecipes[cellType] = legacy;
    render();
  } catch (error) {
    setNotification(error.message, "error", true);
  }
}

function getLotDisplayStatus(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};

  if (!production.CurrentLotId) {
    return "Aucun lot";
  }

  if (production.PauseRequested || live.Result === "PAUSE") {
    return "Pause";
  }

  if (live.Result === "PRESSION AIR") {
    return "Air à vérifier";
  }

  if (live.Result === "ARRÊT URGENCE" || isAutomateStartBlocked(live.Result)) {
    return "Départ bloqué";
  }

  if (live.Result === "ATTENTE START" || live.Result === "ATTENTE") {
    return "Lot chargé";
  }

  if (production.LotControlEnabled) {
    return "Tri en cours";
  }

  return "Lot en attente";
}

function getLotActionHint(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};

  if (!production.CurrentLotId) {
    return "Créer ou charger un lot.";
  }

  if (production.PauseRequested || live.Result === "PAUSE") {
    return "Le lot est en pause. Utiliser DÉMARRER pour repartir.";
  }

  if (live.Result === "PRESSION AIR") {
    return "Pression d'air insuffisante : vérifier l'arrivée d'air, puis acquitter côté machine ou logiciel constructeur.";
  }

  if (live.Result === "ARRÊT URGENCE") {
    return "Arrêt d'urgence appuyé : le relâcher, vérifier la sécurité, puis acquitter côté machine ou logiciel constructeur.";
  }

  if (isAutomateStartBlocked(live.Result)) {
    return "L'automate refuse le départ. Utiliser RÉARMER après contrôle mécanique, puis appuyer sur DÉMARRER.";
  }

  if (live.Result === "ATTENTE START" || live.Result === "ATTENTE") {
    return "Le lot est chargé. Utiliser DÉMARRER pour lancer le tri.";
  }

  return "Le lot est en cours.";
}

function getMachineCommandDetail(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};
  if (!app?.Connected) {
    return "PLC hors ligne";
  }

  if (production.PauseRequested || live.Result === "PAUSE") {
    return "Pause active";
  }

  if (live.Result === "PRESSION AIR") {
    return "Air insuffisant";
  }

  if (live.Result === "ARRÊT URGENCE" || isAutomateStartBlocked(live.Result)) {
    return "Départ bloqué";
  }

  if (live.Result === "ATTENTE START" || live.Result === "ATTENTE") {
    return hasOdooLotAssociation(production)
      ? "Prêt à démarrer"
      : "Prêt à démarrer (traçabilité Odoo optionnelle)";
  }

  return "Tri actif";
}

function renderProduction() {
  const app = state.app;
  if (!app) {
    return;
  }

  const live = app.Live || {};
  const production = app.Production || {};
  const counters = app.Counters || {};
  const alarms = app.Alarms?.Labels || [];
  const lotStatus = getLotDisplayStatus(app);
  const nextAction = getLotActionHint(app);
  const sortingCharacteristics = buildSortingCharacteristicsCard(app, nextAction);
  const qualityIntervalsActive = (production.QualityIntervals || []).length > 0;
  const qualityIntervalMode = production.SortingMode === "INTELLIGENT_GOOD_NG" && Number(production.SampleTarget || 0) === LEARNING_SAMPLE_TARGET;
  const currentLaneLabel = qualityIntervalsActive
    ? (live.TargetLane || production.CurrentGoodLane || "--")
    : (production.CurrentGoodLane || "--");
  const nextLaneLabel = qualityIntervalsActive
    ? "Selon IR"
    : (production.NextGoodLane || "--");

  setHtml("productionPills", [
    badge(production.SortingMode === "LEGACY" ? "Legacy" : "GOOD / NG", "neutral"),
    badge(production.CellType || "--", "live"),
    badge(lotStatus, production.PauseRequested ? "pause" : "neutral")
  ].join(""));

  setHtml("machineStateBlock", `
    <div class="state-tile">
      <span>État machine</span>
      <strong>${escapeHtml(app.Connected ? "Connectée" : "Hors ligne")}</strong>
    </div>
    <div class="state-tile">
      <span>État du lot</span>
      <strong>${escapeHtml(lotStatus)}</strong>
      <div class="muted">${escapeHtml(getOdooLotLabel(production))}</div>
    </div>
    <div class="state-tile">
      <span>Ligne GOOD courante</span>
      <strong>${escapeHtml(currentLaneLabel)}</strong>
    </div>
    <div class="state-tile">
      <span>Prochaine ligne</span>
      <strong>${escapeHtml(nextLaneLabel)}</strong>
    </div>
    ${sortingCharacteristics}
  `);

  setHtml("bigMetrics", `
    <div class="metric-card is-live">
      <div class="metric-label">Tension live</div>
      <strong>${formatNumber(live.Voltage, 4)} V</strong>
      <div class="metric-meta">Voie cible <b>${escapeHtml(live.TargetLane || live.Channel || "--")}</b></div>
    </div>
    <div class="metric-card is-live">
      <div class="metric-label">IR live</div>
      <strong>${formatNumber(live.Ir, 3)} mΩ</strong>
      <div class="metric-meta">Motif <b>${escapeHtml(live.RejectReason || "—")}</b></div>
    </div>
    <div class="metric-card is-neutral">
      <div class="metric-label">Total lot</div>
      <strong>${formatInt(production.TotalCount)}</strong>
      <div class="metric-meta">Compteur lot visible, corrigé par la machine</div>
    </div>
    <div class="metric-card is-good">
      <div class="metric-label">GOOD / NG lot</div>
      <strong>${formatInt(production.GoodCount)} / ${formatInt(production.NgCount)}</strong>
      <div class="metric-meta">${counters.Source === "MACHINE_LOT" ? "Compteurs machine du lot" : "Compteurs machine"} <b>${formatInt(counters.GoodTotal)} / ${formatInt(counters.NgTotal)}</b></div>
    </div>
  `);

  const ngCellsLive = production.RecentNgCells || [];
  setHtml("ngCharacteristics", `
    <div class="ng-focus-card">
      <header>
        <h3>Cellules NG</h3>
        ${badge(`${formatInt(production.NgCount)} NG lot`, production.NgCount > 0 ? "danger" : "live")}
      </header>
      ${ngCellsLive.length
        ? ngCellsLive.slice(-5).reverse().map((cell) => `
          <div class="ng-cell-row">
            <strong title="${escapeHtml(cell.RejectReason || cell.Result || "NG")}">${escapeHtml(cell.RejectReason || cell.Result || "NG")}</strong>
            <span>${formatNumber(cell.Voltage, 4)} V</span>
            <span>${formatNumber(cell.Ir, 3)} mΩ</span>
            <span>${escapeHtml(cell.Timestamp || "")}</span>
          </div>`).join("")
        : `<div class="muted">Aucune cellule NG dans le lot courant.</div>`}
    </div>
  `);

  const alarmHtml = alarms.length
    ? alarms.slice(0, 6).map((label) => `
        <div class="alarm-item is-danger">
          <strong>${escapeHtml(label)}</strong>
          <div class="muted">Alarme prioritaire lue sur la machine</div>
        </div>`).join("")
    : `<div class="alarm-item is-ok"><strong>Aucune alarme active</strong><div class="muted">Les registres d’alarmes ne remontent rien de critique.</div></div>`;
  setHtml("priorityAlarms", alarmHtml);

  setHtml("learningSummary", `
    <div class="stack">
      <div class="note">
        <strong>Statut d’apprentissage</strong>
        <div class="muted">${escapeHtml(production.LearningStatus || "IDLE")}</div>
      </div>
      <div class="note">
        <strong>Échantillon</strong>
        <div class="muted">${formatInt(production.SampleCount)} / ${formatInt(production.SampleTarget)}</div>
      </div>
      <div class="note">
        <strong>Référence tension</strong>
        <div class="muted">Moyenne ${formatNumber(production.MeanVoltage, 4)} · Sigma ${formatNumber(production.SigmaVoltage, 4)}</div>
      </div>
      <div class="note">
        <strong>Référence IR</strong>
        <div class="muted">Moyenne ${formatNumber(production.MeanIr, 3)} · Sigma ${formatNumber(production.SigmaIr, 3)}</div>
      </div>
      <div class="note">
        <strong>Message</strong>
        <div class="muted">${escapeHtml(production.AlertMessage || "Aucun message particulier.")}</div>
      </div>
    </div>
  `);

  const lanes = production.Lanes || [];
  setHtml("laneSummary", qualityIntervalMode
    ? buildLaneFillGraph(production)
    : buildLegacyLaneSummary(lanes));

  const samples = production.RecentSample || [];
  setHtml("sampleGrid", samples.length
    ? `<div class="sample-grid">${samples.map((sample, index) => `
        <div class="sample-card">
          <strong>Cellule ${index + 1}</strong>
          <div class="sample-meta">Tension ${formatNumber(sample.Voltage, 4)} V</div>
          <div class="sample-meta">IR ${formatNumber(sample.Ir, 3)} mΩ</div>
          <div class="sample-meta">${escapeHtml(sample.Timestamp || "")}</div>
        </div>`).join("")}</div>`
    : `<div class="note"><strong>Pas encore de cellules</strong><div class="muted">Démarrer le tri pour voir défiler les cellules ici.</div></div>`);
  setHtml("qualityIntervalChart", buildQualityIntervalChart(production));
}

function buildLaneFillGraph(production) {
  const lanes = production?.Lanes || [];
  const intervals = (production?.QualityIntervals || []).slice().sort((a, b) => Number(a.Index || 0) - Number(b.Index || 0));
  const laneById = (laneId) => lanes.find((lane) => String(lane.LaneId) === String(laneId)) || {};
  const intervalByLane = new Map(intervals.map((interval) => [String(interval.LaneId || interval.Index), interval]));
  const sampleCount = Number(production?.SampleCount || 0);
  const sampleTarget = Math.max(1, Number(production?.SampleTarget || LEARNING_SAMPLE_TARGET));
  const learningPct = Math.max(0, Math.min(100, (sampleCount / sampleTarget) * 100));
  const rows = Array.from({ length: QUALITY_INTERVAL_LANE_COUNT }, (_, index) => {
    const laneId = String(index + 1);
    const lane = laneById(laneId);
    const interval = intervalByLane.get(laneId);
    return {
      laneId,
      count: Math.max(0, Number(lane.CountAssigned || 0)),
      machine: Math.max(0, Number(lane.MachineCount || 0)),
      interval
    };
  });
  const maxCount = Math.max(1, ...rows.map((row) => row.count));

  return `
    <div class="lane-fill-panel">
      <article class="learning-gauge">
        <div>
          <span class="pane-caption">Ligne 10 apprentissage</span>
          <strong>${formatInt(sampleCount)} / ${formatInt(sampleTarget)}</strong>
        </div>
        <div class="mini-progress" aria-label="Progression apprentissage">
          <span style="width:${learningPct.toFixed(2)}%"></span>
        </div>
        <small>${sampleCount >= sampleTarget ? "Modele pret pour les intervalles 1-" + QUALITY_INTERVAL_LANE_COUNT : "Les " + LEARNING_SAMPLE_TARGET + " premieres cellules construisent le modele du lot"}</small>
      </article>
      <div class="lane-fill-list">
        ${rows.map((row) => {
          const fill = Math.max(0, Math.min(100, (row.count / maxCount) * 100));
          const intervalText = row.interval
            ? `${formatNumber(row.interval.IrMin, 3)} - ${formatNumber(row.interval.IrMax, 3)} mOhm`
            : "Intervalle en attente";
          return `
            <article class="lane-fill-row ${row.count > 0 ? "has-cells" : ""}">
              <div class="lane-fill-head">
                <strong>L${escapeHtml(row.laneId)}</strong>
                <span>${formatInt(row.count)} cellule${row.count > 1 ? "s" : ""}</span>
              </div>
              <div class="lane-fill-track">
                <span style="width:${fill.toFixed(2)}%"></span>
              </div>
              <div class="lane-fill-meta">
                <span>${escapeHtml(intervalText)}</span>
                <span>Machine ${formatInt(row.machine)}</span>
              </div>
            </article>`;
        }).join("")}
      </div>
    </div>`;
}

function buildLegacyLaneSummary(lanes) {
  return lanes.length
    ? `<div class="lane-grid">${lanes.map((lane) => {
        const laneOverfilled = (lane.Role || "").toUpperCase() === "GOOD" && Number(lane.CapacityTarget || 0) > 0 && Number(lane.MachineCount || 0) > Number(lane.CapacityTarget || 0);
        const laneTone = laneOverfilled || lane.Status === "BLOCKED" || lane.Status === "FULL" ? "danger" : lane.Status === "NEAR_FULL" ? "pause" : "live";
        return `
        <div class="lane-card">
          <header>
            <strong>Ligne ${escapeHtml(lane.LaneId)}</strong>
            ${badge(laneOverfilled ? "SUR-REMPLIE" : (lane.Status || "AVAILABLE"), laneTone)}
          </header>
          <div class="lane-meta">Role ${escapeHtml(lane.Role || "--")}</div>
          <div class="lane-meta">Affectees ${formatInt(lane.CountAssigned)} / cible ${formatInt(lane.CapacityTarget)}</div>
          <div class="lane-meta">Machine ${formatInt(lane.MachineCount)} - ecart ${formatInt((lane.MachineCount || 0) - (lane.CountAssigned || 0))}</div>
        </div>`;
      }).join("")}</div>`
    : `<div class="note"><strong>Aucune ligne chargee</strong><div class="muted">Cree ou charge un lot pour initialiser les lignes GOOD.</div></div>`;
}

function buildQualityIntervalChart(production) {
  const intervals = (production?.QualityIntervals || []).slice().sort((a, b) => Number(a.Index || 0) - Number(b.Index || 0));
  if (!intervals.length) {
    const sampleCount = formatInt(production?.SampleCount || 0);
    const sampleTarget = formatInt(production?.SampleTarget || LEARNING_SAMPLE_TARGET);
    return `
      <div class="interval-empty note">
        <strong>Intervalles pas encore figes</strong>
        <div class="muted">La ligne 10 collecte ${sampleCount} / ${sampleTarget} cellules. Le modele se fige uniquement a ${LEARNING_SAMPLE_TARGET} cellules, puis les lignes 1 a ${QUALITY_INTERVAL_LANE_COUNT} deviennent des intervalles IR fixes.</div>
      </div>`;
  }

  const minIr = Math.min(...intervals.map((item) => Number(item.IrMin || 0)));
  const maxIr = Math.max(...intervals.map((item) => Number(item.IrMax || 0)));
  const span = Math.max(0.001, maxIr - minIr);
  const voltageMin = intervals[0]?.VoltageMin;
  const voltageMax = intervals[0]?.VoltageMax;
  const sampleCount = intervals[0]?.LearningSampleCount || production?.SampleCount || 0;

  return `
    <div class="interval-summary">
      <div>
        <span class="pane-caption">Classement par resistance</span>
        <strong>${formatNumber(minIr, 3)} - ${formatNumber(maxIr, 3)} mOhm</strong>
      </div>
      <div>
        <span class="pane-caption">Garde tension</span>
        <strong>${formatNumber(voltageMin, 4)} - ${formatNumber(voltageMax, 4)} V</strong>
      </div>
      <div>
        <span class="pane-caption">Echantillon fige</span>
        <strong>${formatInt(sampleCount)} cellules ligne 10</strong>
      </div>
    </div>
    <div class="interval-scroll">
      <div class="interval-axis">
        <span>${formatNumber(minIr, 3)} mOhm</span>
        <span>${formatNumber(maxIr, 3)} mOhm</span>
      </div>
      <div class="interval-bars">
        ${intervals.map((interval) => {
          const left = ((Number(interval.IrMin || 0) - minIr) / span) * 100;
          const width = Math.max(2.4, ((Number(interval.IrMax || 0) - Number(interval.IrMin || 0)) / span) * 100);
          return `
            <article class="interval-row">
              <div class="interval-lane">Ligne ${escapeHtml(interval.LaneId || interval.Index || "--")}</div>
              <div class="interval-track">
                <div class="interval-bar" style="left:${left.toFixed(3)}%;width:${width.toFixed(3)}%">
                  <span>
                    <b>${formatNumber(interval.IrMin, 3)}</b>
                    <b>${formatNumber(interval.IrMax, 3)}</b>
                  </span>
                </div>
              </div>
              <div class="interval-range">${formatNumber(interval.IrMin, 3)} / ${formatNumber(interval.IrMax, 3)} mOhm</div>
            </article>`;
        }).join("")}
      </div>
    </div>`;
}

async function runLotAction(action) {
  try {
    const result = await apiPost(`/api/lots/${action}`, {});
    const lotId = result?.Lot?.Id ? `#${result.Lot.Id}` : "";
    if (action === "reset-lines") {
      setNotification(result.Message || `Apprentissage du lot ${lotId} remis à zéro.`, "success");
      await refreshAll(true);
      return;
    }
    if (action === "confirm-empty-lines") {
      setNotification(result.Message || `Bacs vidés confirmés pour le lot ${lotId}.`, "success");
      await refreshAll(true);
      return;
    }
    const messageMap = {
      new: `Lot ${lotId} créé. Utiliser Lancer le tri pour démarrer.`,
      continue: `Lot ${lotId} chargé. Utiliser Lancer le tri pour repartir.`,
      close: `Lot ${lotId} clôturé.`
    };
    setNotification(messageMap[action] || result.Message || `Lot ${action}`, action === "close" ? "info" : "success");
    await refreshAll(true);
  } catch (error) {
    setNotification(error.message, "error");
  }
}

async function bootstrap() {
  bindGlobalEvents();
  await refreshAll(true);
  await searchOdooLots("", true);

  state.refreshHandle = window.setInterval(() => refreshAll(false), 1000);
  state.auxHandle = window.setInterval(() => refreshAuxiliary(), 5000);
}

window.addEventListener("DOMContentLoaded", bootstrap);

function humanConnectionLabel(app) {
  if (!app) {
    return "Initialisation...";
  }

  if (!app.Connected) {
    return "Machine hors ligne";
  }

  return `Machine connectée - ${app.Config?.ComPort || "--"} - ${app.Config?.SortingMode || "--"}`;
}

function renderTopbar() {
  const app = state.app;
  const production = app?.Production || {};
  const connected = !!app?.Connected;
  const odooLabel = getOdooLotLabel(production);
  const modeLabel = app?.Config?.SortingMode === "INTELLIGENT_GOOD_NG" ? "GOOD / NG" : (app?.Config?.SortingMode || "--");
  const counterLabel = app?.Counters?.Source || "--";
  const lotDetail = production.CurrentLotId
    ? `#${production.CurrentLotId} · ${production.CellType || app?.Config?.CellType || "--"} · ${production.LotStatus || "ACTIVE"}`
    : "Aucun lot local";
  const qualityIntervalsActive = (production.QualityIntervals || []).length > 0;
  const laneStateDetail = qualityIntervalsActive
    ? `${lotDetail} · tri selon IR · voie live ${app?.Live?.TargetLane || app?.Live?.Channel || "--"}`
    : (production.CurrentGoodLane ? `${lotDetail} · ligne ${production.CurrentGoodLane} -> ${production.NextGoodLane || "--"}` : lotDetail);

  setText("topbarStatus", `${connected ? "Connectée" : "Hors ligne"} · ${app?.Config?.ComPort || "--"} · ${modeLabel} · ${counterLabel}`);
  setText("commandMachineState", connected ? "Connectée" : "Hors ligne");
  setText("commandMachineDetail", getMachineCommandDetail(app));
  setText("commandLotState", odooLabel);
  setText("commandLaneState", laneStateDetail);
  setText(
    "commandStartGuard",
    hasOdooLotAssociation(production)
      ? "Lot suivi associé : traçabilité Odoo active"
      : (connected ? "DÉMARRER autorisé sans lot Odoo : traçabilité locale seulement" : "PLC hors ligne")
  );
  setInputValueIfFree("odooLotReferenceInput", production.OdooLotReference || "");
  setInputValueIfFree("odooLotNameInput", production.OdooLotName || "");
  setInputValueIfFree("odooProductInput", production.OdooProductReference || production.OdooProductName || "");
  updateCommandAvailability(app);
}

function getOdooLotLabel(production) {
  if (!production) {
    return "Lot suivi non associé";
  }

  const label = production.OdooLotReference || production.OdooLotName;
  if (!label) {
    return "Lot suivi non associé";
  }

  return production.OdooVerified ? label : `${label} non vérifié`;
}

function setInputValueIfFree(id, value) {
  const node = document.getElementById(id);
  if (node && document.activeElement !== node) {
    node.value = value == null ? "" : String(value);
  }
}

function setInputValue(id, value) {
  const node = document.getElementById(id);
  if (node) {
    node.value = value == null ? "" : String(value);
  }
}

function hasOdooLotAssociation(production) {
  return !!(production && production.OdooVerified && (production.OdooLotReference || production.OdooLotName));
}

function isAutomateStartBlocked(result) {
  return result === "DÉPART BLOQUÉ";
}

function hasBlockingMachineResult(result) {
  return result === "PRESSION AIR" ||
    result === "ARRÊT URGENCE" ||
    isAutomateStartBlocked(result);
}

function getMachineAlarmNotice(app) {
  const live = app?.Live || {};
  const alarms = app?.Alarms?.Labels || [];
  const statusValue = app?.Diagnostic?.StatusValue;
  const suffix = statusValue == null || statusValue === ""
    ? ""
    : ` · statut automate ${statusValue}`;

  if (live.Result === "PRESSION AIR") {
    return {
      title: "PRESSION AIR",
      detail: "Arrivée d'air insuffisante : vérifier compresseur, vanne et alimentation d'air, puis acquitter côté machine ou logiciel constructeur.",
      meta: `${alarms.length ? alarms.join(", ") : (live.RejectReason || "Pression d'air insuffisante")}${suffix}`,
      blocking: true
    };
  }

  if (live.Result === "ARRÊT URGENCE") {
    return {
      title: "ARRÊT D'URGENCE",
      detail: "Arrêt d'urgence actif : le relâcher, vérifier la sécurité, puis acquitter côté machine ou logiciel constructeur.",
      meta: `${alarms.length ? alarms.join(", ") : (live.RejectReason || "Arrêt d'urgence actif")}${suffix}`,
      blocking: true
    };
  }

  if (isAutomateStartBlocked(live.Result)) {
    return {
      title: "DÉPART BLOQUÉ",
      detail: "L'automate refuse le départ. Utiliser RÉARMER après contrôle mécanique, puis relancer DÉMARRER.",
      meta: `${alarms.length ? alarms.join(", ") : (live.RejectReason || "Départ bloqué par l'automate")}${suffix}`,
      blocking: true
    };
  }

  if (alarms.length) {
    return {
      title: "INFO MACHINE",
      detail: "Alarme non bloquante pour DÉMARRER : l'information reste visible, mais le logiciel ne bloque pas le départ.",
      meta: `${alarms.join(", ")}${suffix}`,
      blocking: false
    };
  }

  return null;
}

function updateCommandAvailability(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};
  const notice = getMachineAlarmNotice(app);
  const machineBlocked = hasBlockingMachineResult(live.Result);
  const startAllowed = !!app?.Connected;
  const blockedTitle = live.Result === "PRESSION AIR"
    ? "Cliquer pour tenter le démarrage. Si l'air manque encore, la machine refusera avec le message pression air."
    : live.Result === "ARRÊT URGENCE"
      ? "Cliquer pour tenter le démarrage. Si l'arrêt d'urgence est encore actif, la machine refusera."
      : "Cliquer pour tenter le démarrage. Si le statut 7 revient, utiliser RÉARMER puis réessayer.";

  const alarmBanner = document.getElementById("machineAlarmBanner");
  if (alarmBanner) {
    alarmBanner.classList.toggle("hidden", !notice);
    alarmBanner.innerHTML = notice
      ? `<strong>${escapeHtml(notice.title)}</strong><span>${escapeHtml(notice.detail)}</span><small>${escapeHtml(notice.meta)}</small>`
      : "";
  }

  setText(
    "commandStartGuard",
    notice?.blocking
      ? `${notice.title} - ${notice.detail}`
      : notice
        ? `DÉMARRER autorisé. ${notice.meta}`
      : (hasOdooLotAssociation(production)
        ? "Lot suivi associé : traçabilité Odoo active"
        : (app?.Connected ? "DÉMARRER autorisé sans lot Odoo : traçabilité locale seulement" : "PLC hors ligne"))
  );

  document.querySelectorAll('[data-command="START"]').forEach((button) => {
    button.dataset.logicDisabled = String(!startAllowed);
    button.title = startAllowed
      ? (machineBlocked ? blockedTitle : "Démarrer la machine")
      : (app?.Connected ? blockedTitle : "PLC hors ligne");
  });

  document.querySelectorAll('[data-command="RESET"]').forEach((button) => {
    button.dataset.logicDisabled = String(!app?.Connected);
    button.title = app?.Connected
      ? "Réarmer l'automate si le statut 7 bloque le départ"
      : "PLC hors ligne";
  });

  document.querySelectorAll('[data-command="PAUSE"], [data-command="STOP"]').forEach((button) => {
    button.dataset.logicDisabled = String(!app?.Connected);
    button.title = app?.Connected ? button.title : "PLC hors ligne";
  });

  const dock = document.querySelector(".control-dock");
  if (dock) {
    dock.classList.toggle("needs-lot", !hasOdooLotAssociation(production));
    dock.classList.toggle("machine-alarm-active", !!notice?.blocking);
  }

  applyInteractionLocks();
}

function clampNumber(value, min, max) {
  const numericValue = Number(value);
  const floor = Number.isFinite(Number(min)) ? Number(min) : numericValue;
  const ceil = Number.isFinite(Number(max)) ? Number(max) : numericValue;

  if (!Number.isFinite(numericValue)) {
    return floor;
  }

  return Math.min(Math.max(numericValue, floor), ceil);
}

function computeSampleValues(sample, key) {
  return (sample || [])
    .map((point) => Number(point?.[key]))
    .filter((value) => Number.isFinite(value))
    .sort((a, b) => a - b);
}

function computeSampleWindow(sample, key) {
  const values = computeSampleValues(sample, key);

  if (!values.length) {
    return 0;
  }

  const center = values.reduce((total, value) => total + value, 0) / values.length;
  return values.reduce((maxDeviation, value) => Math.max(maxDeviation, Math.abs(value - center)), 0);
}

function computeQuantile(sortedValues, quantile) {
  if (!sortedValues.length) {
    return 0;
  }

  if (sortedValues.length === 1) {
    return sortedValues[0];
  }

  const index = (sortedValues.length - 1) * quantile;
  const lowerIndex = Math.floor(index);
  const upperIndex = Math.ceil(index);
  if (lowerIndex === upperIndex) {
    return sortedValues[lowerIndex];
  }

  const ratio = index - lowerIndex;
  return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * ratio);
}

function computeRobustIqrWindow(sample, key, minWindow, maxWindow) {
  const values = computeSampleValues(sample, key);
  if (values.length < 10) {
    return null;
  }

  const q1 = computeQuantile(values, 0.25);
  const q3 = computeQuantile(values, 0.75);
  const iqr = q3 - q1;
  if (!Number.isFinite(iqr) || iqr < 0) {
    return null;
  }

  const lower = q1 - (2.0 * iqr);
  const upper = q3 + (2.0 * iqr);
  const center = values.reduce((total, value) => total + value, 0) / values.length;
  const robustWindow = Math.max(Math.abs(center - lower), Math.abs(upper - center));
  const observedWindow = values.reduce((maxDeviation, value) => Math.max(maxDeviation, Math.abs(value - center)), 0) * 1.10;
  return clampNumber(Math.max(robustWindow, observedWindow, minWindow), minWindow, maxWindow);
}

function computeAdaptiveWindow(sample, acceptanceK, sigma, minWindow, maxWindow, key) {
  const safeMin = Math.abs(Number(minWindow || 0));
  const safeMax = Math.max(safeMin, Math.abs(Number(maxWindow || safeMin)));
  const robustWindow = computeRobustIqrWindow(sample, key, safeMin, safeMax);
  if (robustWindow !== null) {
    return robustWindow;
  }

  const sigmaWindow = Math.abs(Number(acceptanceK || 0)) * Math.abs(Number(sigma || 0));
  const sampleWindow = computeSampleWindow(sample, key);
  const paddedRangeWindow = sampleWindow * 1.10;
  return clampNumber(Math.max(sigmaWindow, paddedRangeWindow, safeMin), safeMin, safeMax);
}

function formatRange(minValue, maxValue, digits, unit) {
  const min = Number(minValue);
  const max = Number(maxValue);
  if (!Number.isFinite(min) || !Number.isFinite(max)) {
    return "--";
  }

  return `${min.toFixed(digits)} → ${max.toFixed(digits)} ${unit}`;
}

function getActiveLegacyThreshold(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};
  const cellType = production.CellType || app?.Config?.CellType || "21700";
  const legacyRecipe = state.legacyRecipes[cellType];
  const laneId = production.CurrentGoodLane || live.TargetLane || live.Channel;
  const laneIndex = Number.parseInt(laneId, 10) - 1;

  if (!legacyRecipe?.Thresholds?.Channels || !Number.isFinite(laneIndex) || laneIndex < 0) {
    return null;
  }

  const threshold = legacyRecipe.Thresholds.Channels[laneIndex];
  if (!threshold) {
    return null;
  }

  return {
    laneId: String(laneId),
    voltageMin: threshold.VoltageMin,
    voltageMax: threshold.VoltageMax,
    irMin: threshold.IrMin,
    irMax: threshold.IrMax,
    modeLabel: legacyRecipe.JudgeMode || app?.Config?.JudgeMode || "BOTH"
  };
}

function getActiveIntelligentThreshold(app) {
  const production = app?.Production || {};
  const live = app?.Live || {};
  const cellType = production.CellType || app?.Config?.CellType || "21700";
  const recipe = state.intelligentRecipes[cellType];
  const laneId = production.CurrentGoodLane || live.TargetLane || live.Channel || "--";
  const sampleCount = Number(production.SampleCount || 0);
  const sampleTarget = Number(production.SampleTarget || recipe?.SampleSize || LEARNING_SAMPLE_TARGET);
  const stableReference = production.LearningStatus === "STABLE" &&
    Number.isFinite(Number(production.MeanVoltage)) &&
    Number.isFinite(Number(production.MeanIr)) &&
    sampleCount > 0;
  const confirmationActive = stableReference && sampleTarget > 0 && sampleCount < sampleTarget;

  if (!recipe) {
    return {
      laneId: String(laneId),
      stableReference: false,
      voltageText: "--",
      irText: "--",
      referenceText: "Recette en chargement",
      ruleText: "Fenêtre indisponible",
      footer: production.AlertMessage || "Lecture des recettes en cours.",
      confirmationActive: false
    };
  }

  if (!stableReference) {
    return {
      laneId: "10",
      stableReference: false,
      voltageText: "Apprentissage",
      irText: "Apprentissage",
      referenceText: `${formatInt(production.SampleCount)} / ${formatInt(production.SampleTarget || LEARNING_SAMPLE_TARGET)} cellules`,
      ruleText: "Ligne 10 apprentissage " + LEARNING_SAMPLE_TARGET,
      footer: production.AlertMessage || "La ligne 10 collecte " + LEARNING_SAMPLE_TARGET + " cellules pour figer les " + QUALITY_INTERVAL_LANE_COUNT + " intervalles resistance.",
      confirmationActive: false
    };
  }

  const sample = production.RecentSample || [];
  const windowVoltage = computeAdaptiveWindow(
    sample,
    recipe.AcceptanceKVoltage,
    production.SigmaVoltage,
    recipe.MinWindowVoltage,
    recipe.MaxWindowVoltage,
    "Voltage"
  );
  const windowIr = computeAdaptiveWindow(
    sample,
    recipe.AcceptanceKIr,
    production.SigmaIr,
    recipe.MinWindowIr,
    recipe.MaxWindowIr,
    "Ir"
  );
  const displayWindowVoltage = confirmationActive
    ? Math.max(Math.abs(Number(recipe.MaxWindowVoltage || 0.12)), Math.abs(Number(recipe.MinWindowVoltage || 0.001)))
    : windowVoltage;
  const displayWindowIr = confirmationActive
    ? Math.max(Math.abs(Number(recipe.MaxWindowIr || 4)), Math.abs(Number(recipe.MinWindowIr || 0.25)))
    : windowIr;

  return {
    laneId: String(laneId),
    stableReference: true,
    voltageText: formatRange(production.MeanVoltage - displayWindowVoltage, production.MeanVoltage + displayWindowVoltage, 4, "V"),
    irText: formatRange(production.MeanIr - displayWindowIr, production.MeanIr + displayWindowIr, 3, "mΩ"),
    referenceText: `${formatNumber(production.MeanVoltage, 4)} V / ${formatNumber(production.MeanIr, 3)} mΩ`,
    ruleText: "9 intervalles IR lignes 1 a 9",
    footer: production.AlertMessage || "Modele lot stable : resistance pour la ligne, tension pour NG sous/surcharge.",
    confirmationActive
  };
}

function buildSortingCharacteristicsCard(app, fallbackMessage) {
  const production = app?.Production || {};
  const sortingMode = production.SortingMode || app?.Config?.SortingMode || "INTELLIGENT_GOOD_NG";

  if (sortingMode === "LEGACY") {
    const legacy = getActiveLegacyThreshold(app);
    const laneLabel = legacy?.laneId || "--";
    return `
      <div class="state-panel state-panel-wide">
        <header>
          <div class="state-panel-title">
            <strong>Caractéristiques de tri</strong>
            <span>Canal legacy ${escapeHtml(laneLabel)}</span>
          </div>
          ${badge("LEGACY", "neutral")}
        </header>
        <div class="state-spec-grid">
          <div class="state-spec">
            <span>Voltage</span>
            <strong>${escapeHtml(legacy ? formatRange(legacy.voltageMin, legacy.voltageMax, 4, "V") : "--")}</strong>
          </div>
          <div class="state-spec">
            <span>Résistance</span>
            <strong>${escapeHtml(legacy ? formatRange(legacy.irMin, legacy.irMax, 3, "mΩ") : "--")}</strong>
          </div>
          <div class="state-spec">
            <span>Mode de jugement</span>
            <strong>${escapeHtml(legacy?.modeLabel || app?.Config?.JudgeMode || "--")}</strong>
          </div>
          <div class="state-spec">
            <span>Règle NG</span>
            <strong>Hors bornes programmées</strong>
          </div>
        </div>
        <div class="muted">${escapeHtml(legacy ? "Seuils legacy affichés pour la voie actuellement pilotée." : "Recette legacy indisponible pour le moment.")}</div>
      </div>
    `;
  }

  let intelligent = getActiveIntelligentThreshold(app);
  const qualityIntervals = (production.QualityIntervals || []).slice().sort((a, b) => Number(a.Index || 0) - Number(b.Index || 0));
  if (qualityIntervals.length) {
    const activeLaneId = production.CurrentGoodLane || app?.Live?.TargetLane || app?.Live?.Channel;
    const activeInterval = qualityIntervals.find((interval) => String(interval.LaneId) === String(activeLaneId)) || qualityIntervals[0];
    intelligent = {
      ...intelligent,
      laneId: activeLaneId || activeInterval.LaneId,
      stableReference: true,
      voltageText: formatRange(activeInterval.VoltageMin, activeInterval.VoltageMax, 4, "V"),
      irText: formatRange(activeInterval.IrMin, activeInterval.IrMax, 3, "mÎ©"),
      referenceText: `${formatInt(activeInterval.LearningSampleCount || production.SampleCount)} cellules ligne 10`,
      ruleText: "Resistance = lignes 1 a 9; tension = garde sous/surcharge",
      footer: production.AlertMessage || "Les intervalles sont figes pour ce lot et ne bougent plus pendant la production.",
      confirmationActive: false
    };
  }
  const statusBadge = intelligent.confirmationActive
    ? badge("CONFIRMATION", "pause")
    : (intelligent.stableReference ? badge("INTELLIGENT", "live") : badge("APPRENTISSAGE", "pause"));
  return `
    <div class="state-panel state-panel-wide">
      <header>
        <div class="state-panel-title">
          <strong>Caractéristiques de tri</strong>
          <span>Ligne GOOD ${escapeHtml(intelligent.laneId || "--")}</span>
        </div>
        ${statusBadge}
      </header>
      <div class="state-spec-grid">
        <div class="state-spec">
          <span>Voltage</span>
          <strong>${escapeHtml(intelligent.voltageText)}</strong>
        </div>
        <div class="state-spec">
          <span>Résistance</span>
          <strong>${escapeHtml(intelligent.irText)}</strong>
        </div>
        <div class="state-spec">
          <span>Référence lot</span>
          <strong>${escapeHtml(intelligent.referenceText)}</strong>
        </div>
        <div class="state-spec">
          <span>Règle NG</span>
          <strong>${escapeHtml(intelligent.ruleText)}</strong>
        </div>
      </div>
      <div class="muted">${escapeHtml(intelligent.footer || fallbackMessage || "")}</div>
    </div>
  `;
}

function scheduleOdooSearch(value) {
  state.odooSearchQuery = (value || "").trim();
  window.clearTimeout(state.odooSearchHandle);
  if (state.odooSearchQuery.length > 0 && state.odooSearchQuery.length < 2) {
    state.odooSearchStatus = "Tape au moins 2 caractères pour chercher.";
    renderOdooSuggestions();
    return;
  }

  state.odooSearchStatus = state.odooSearchQuery ? "Recherche en cours..." : "";
  renderOdooSuggestions();
  state.odooSearchHandle = window.setTimeout(() => searchOdooLots(state.odooSearchQuery, true), 260);
}

async function searchOdooLots(query = "", silent = false) {
  state.odooSearchQuery = (query || "").trim();
  if (!silent) {
    state.odooSearchStatus = "Recherche en cours...";
    renderOdooSuggestions();
  }

  try {
    const payload = await apiGet(`/api/odoo/lots?q=${encodeURIComponent(state.odooSearchQuery)}&limit=8`);
    state.odooCandidates = payload.lots || [];
    state.odooLiveConfigured = !!payload.live_configured;
    state.odooSearchStatus = "";
  } catch (error) {
    state.odooCandidates = [];
    state.odooSearchStatus = `Recherche lots indisponible : ${error.message}`;
  }

  renderOdooSuggestions();
}

function renderOdooSuggestions() {
  const node = document.getElementById("odooSuggestions");
  if (!node) {
    return;
  }

  const suggestions = state.odooCandidates || [];
  const query = state.odooSearchQuery || "";

  if (state.odooSearchStatus) {
    node.innerHTML = `<span class="odoo-empty">${escapeHtml(state.odooSearchStatus)}</span>`;
    return;
  }

  if (!suggestions.length && query) {
    const sourceHint = state.odooLiveConfigured
      ? "Aucun lot suivi Odoo trouvé pour cette recherche."
      : "Connexion Odoo live non configurée sur ce PC, et aucun lot suivi dans le cache local.";
    node.innerHTML = `<span class="odoo-empty">${escapeHtml(sourceHint)} Le démarrage reste possible sans lot Odoo, avec une traçabilité locale uniquement.</span>`;
    return;
  }

  if (!suggestions.length) {
    node.innerHTML = `<span class="odoo-empty">${state.odooLiveConfigured ? "Cherche un lot de cellules suivi dans Odoo ou scanne sa référence." : "Connexion Odoo live non configurée sur ce PC. Le tri peut quand même démarrer avec une traçabilité locale."}</span>`;
    return;
  }

  const options = suggestions.map((lot, index) => {
    const label = [
      lot.Reference || lot.Name,
      lot.ProductReference || lot.ProductName,
      lot.Quantity ? `Qté ${lot.Quantity}` : ""
    ].filter(Boolean).join(" - ");
    return `<option value="${index}">${escapeHtml(label)}</option>`;
  }).join("");

  node.innerHTML = `
    <label class="odoo-select-wrap">
      <span>Lot disponible</span>
      <select id="odooLotSelect" data-odoo-select>
        <option value="">Choisir un lot Odoo...</option>
        ${options}
      </select>
    </label>
    <div class="odoo-select-hint">${escapeHtml(suggestions.length)} lot${suggestions.length > 1 ? "s" : ""} disponible${suggestions.length > 1 ? "s" : ""}. Sélection = association immédiate.</div>
  `;
}

function renderStats() {
  const app = state.app;
  if (!app) {
    return;
  }

  const production = app.Production || {};
  const counters = app.Counters || {};
  const cards = [
    { label: "État machine", value: app.Connected ? "Connectée" : "Hors ligne" },
    { label: "Lot cellules Odoo", value: getOdooLotLabel(production) },
    { label: "Mode", value: app.Config?.SortingMode === "LEGACY" ? "Legacy" : "Good / NG" },
    { label: "Cellule", value: app.Config?.CellType || "--" },
    { label: "Total machine", value: formatInt(counters.Total) },
    { label: "Good / NG", value: `${formatInt(counters.GoodTotal)} / ${formatInt(counters.NgTotal)}` }
  ];

  setHtml("stats", cards.map((card) => `
    <article class="stat-card">
      <div class="stat-label">${escapeHtml(card.label)}</div>
      <div class="stat-value">${escapeHtml(card.value)}</div>
    </article>
  `).join(""));
}

function renderTabs() {
  document.querySelectorAll(".chip[data-tab]").forEach((button) => {
    const active = button.dataset.tab === state.activeTab;
    button.classList.toggle("is-active", active);
    button.classList.toggle("active", active);
  });

  document.querySelectorAll(".view").forEach((section) => {
    section.classList.toggle("is-active", section.dataset.view === state.activeTab);
  });

  const maintenanceTabButton = document.getElementById("maintenanceTabButton");
  if (maintenanceTabButton) {
    maintenanceTabButton.classList.toggle("hidden", !state.maintenanceVisible);
  }
}

function render(options = {}) {
  const preserveScroll = options.preserveScroll !== false;
  const scrollSnapshot = preserveScroll ? captureScrollState() : null;
  renderTabs();
  renderTopbar();
  renderStats();
  renderProduction();
  renderRecipes();
  renderHistory();
  renderDiagnostic();
  renderMaintenance();
  applyInteractionLocks();
  if (preserveScroll) {
    restoreScrollState(scrollSnapshot);
    window.requestAnimationFrame(() => restoreScrollState(scrollSnapshot));
  }
}
