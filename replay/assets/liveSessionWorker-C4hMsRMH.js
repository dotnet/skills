(function() {
	function Se(t, e, n = 0) {
		return Math.max(t - e - Math.max(n, 0), 0);
	}
	function Ne(t, e, n) {
		return Se(t, n, e) + Math.max(e, 0) + Math.max(n, 0);
	}
	function V(t, e, n) {
		var s = Ne(t, e, n);
		if (!(s <= 0)) return Math.max(n, 0) / s;
	}
	function et(t) {
		if (!t || t.length === 0) return 0;
		let e = 0;
		for (let n = 0; n < t.length; n += 1) {
			const s = t[n].t + t[n].duration;
			s > e && (e = s);
		}
		return e;
	}
	function M(t, e) {
		return t ? t.length > e ? t.slice(0, e) + "..." : t : "";
	}
	const we = 4e3, Re = "ATIF-v1.6", Ae = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})$/, Oe = /^Error/i, De = /exited with exit code [1-9]/;
	function Ut(t) {
		try {
			return JSON.parse(t);
		} catch {
			return;
		}
	}
	function je(t) {
		const e = Ut(t);
		if (!e || typeof e != "object" || Array.isArray(e)) return !1;
		const n = e;
		return typeof n.schema_version == "string" && n.schema_version.startsWith("ATIF-");
	}
	function Le(t) {
		if (!t) return null;
		const e = new Date(t);
		return Number.isNaN(e.getTime()) ? null : e.getTime() / 1e3;
	}
	function We(t) {
		if (!Ae.test(t)) return !1;
		const e = new Date(t);
		return !Number.isNaN(e.getTime());
	}
	function ct(t) {
		if (t == null) return "";
		if (typeof t == "string") return t;
		if (!Array.isArray(t)) return "";
		const e = [];
		for (let n = 0; n < t.length; n += 1) {
			const s = t[n];
			if (!s || typeof s != "object") continue;
			const o = typeof s.type == "string" ? s.type.toLowerCase() : "";
			if (o === "image" || o === "image_url" || s.path || s.url) {
				const i = s.path || s.url || "";
				e.push("[image: " + i + "]");
			} else typeof s.text == "string" && e.push(s.text);
		}
		return e.join(`
`);
	}
	function Pe(t) {
		return t === "agent" ? "assistant" : t === "user" ? "user" : "system";
	}
	function Ue(t) {
		return t === "system" ? "context" : "output";
	}
	function qe(t, e) {
		const n = (a) => a.replace(/^ATIF-v/i, ""), s = n(t).split(/[.\-+]/).map(function(a) {
			const r = parseInt(a, 10);
			return Number.isNaN(r) ? 0 : r;
		}), o = n(e).split(/[.\-+]/).map(function(a) {
			const r = parseInt(a, 10);
			return Number.isNaN(r) ? 0 : r;
		}), i = Math.max(s.length, o.length);
		for (let a = 0; a < i; a += 1) {
			const r = (s[a] || 0) - (o[a] || 0);
			if (r !== 0) return r > 0 ? 1 : -1;
		}
		return 0;
	}
	function Fe(t, e) {
		const n = t.map(function(r) {
			return r.timestamp == null ? null : typeof r.timestamp != "string" || !We(r.timestamp) ? (e.push("Step " + (r.step_id ?? "?") + " has malformed timestamp: " + r.timestamp), null) : Le(r.timestamp);
		});
		let s = 0, o = !1;
		for (let r = 0; r < n.length; r += 1) if (n[r] != null) {
			s = n[r], o = !0;
			break;
		}
		if (!o) return n.map(function(r, l) {
			return l;
		});
		const i = new Array(n.length);
		let a = s;
		for (let r = 0; r < n.length; r += 1) {
			const l = n[r];
			if (l != null) {
				a = l, i[r] = l;
				continue;
			}
			let g = r + 1;
			for (; g < n.length && n[g] == null;) g += 1;
			if (g < n.length) {
				const _ = n[g], c = g - r + 1, x = (_ - a) / c;
				i[r] = a + x, a = i[r];
			} else i[r] = a;
		}
		return i;
	}
	function ft(t, e, n, s, o, i, a, r) {
		const l = {
			t,
			agent: e,
			track: n,
			text: M(s, we),
			duration: o,
			intensity: i,
			raw: a,
			turnIndex: 0,
			isError: !1
		};
		return r && Object.assign(l, r), l;
	}
	function qt(t) {
		if (!t) return 0;
		const e = [
			"duration_ms",
			"latency_ms",
			"elapsed_ms"
		], n = [
			t,
			t.metrics,
			t.extra
		];
		for (const s of n) if (s) for (const o of e) {
			const i = s[o];
			if (typeof i == "number" && isFinite(i) && i > 0) return i / 1e3;
		}
		return 0;
	}
	function ze(t) {
		if (!t) return null;
		const e = t.prompt_tokens || 0, n = t.completion_tokens || 0, s = t.cached_tokens || 0;
		return e + n + s === 0 ? null : {
			inputTokens: e,
			outputTokens: n,
			cacheRead: s,
			cacheHitRate: V(e, 0, s)
		};
	}
	function Be(t) {
		return t.is_copied_context === !0;
	}
	function Je(t) {
		const e = Ut(t);
		if (!e || typeof e != "object" || Array.isArray(e)) return null;
		const n = e;
		if (!n.agent || typeof n.agent != "object" || !Array.isArray(n.steps)) return null;
		const s = [];
		typeof n.schema_version == "string" && n.schema_version.startsWith("ATIF-") && qe(n.schema_version, Re) > 0 && s.push("Schema version " + n.schema_version + " is newer than supported ATIF-v1.6; parsing best-effort.");
		const o = n.steps;
		if (o.length === 0) return null;
		for (let C = 0; C < o.length; C += 1) {
			const T = o[C], E = C + 1;
			typeof T.step_id == "number" && T.step_id !== E && s.push("Non-sequential step_id at position " + C + " (expected " + E + ", got " + T.step_id + ")");
		}
		const i = Fe(o, s), a = i.length > 0 ? i[0] : 0, r = [], l = [];
		let g = null, _ = n.agent.model_name;
		for (let C = 0; C < o.length; C += 1) {
			const T = o[C], E = i[C] - a, O = C + 1 < o.length ? i[C + 1] - a : E + (T.metrics && T.metrics.extra && typeof T.metrics.extra.model_call_duration_ms == "number" ? T.metrics.extra.model_call_duration_ms / 1e3 : 0), H = Math.max(0, O - E), B = Pe(T.source), J = Be(T), Q = T.model_name || _ || null;
			if (T.observation && Array.isArray(T.observation.results) && Array.isArray(T.tool_calls)) {
				const D = {};
				for (let S = 0; S < T.tool_calls.length; S += 1) {
					const W = T.tool_calls[S].tool_call_id;
					typeof W == "string" && (D[W] = !0);
				}
				for (let S = 0; S < T.observation.results.length; S += 1) {
					const W = T.observation.results[S];
					typeof W.source_call_id == "string" && !D[W.source_call_id] && s.push("Step " + (T.step_id ?? C + 1) + " observation references unknown tool_call_id " + W.source_call_id);
				}
			}
			const ut = C;
			g && l.push(g);
			const vs = T.source === "user" ? ct(T.message) : "";
			g = {
				index: ut,
				startTime: E,
				endTime: E,
				eventIndices: [],
				userMessage: T.source === "user" ? vs || "(continuation)" : B,
				toolCount: 0,
				hasError: !1
			};
			const mt = {
				step_id: T.step_id,
				source: T.source,
				timestamp: T.timestamp,
				isCopiedContext: J,
				reasoningEffort: T.reasoning_effort,
				stepCostUsd: T.metrics && typeof T.metrics.cost_usd == "number" ? T.metrics.cost_usd : null
			}, Es = ze(T.metrics), Ie = T.extra && typeof T.extra.ttft_ms == "number" ? T.extra.ttft_ms : 0, be = Ie > 0 ? Ie / 1e3 : 0, Ms = H > 0 ? Math.min(be, H) : be, Ce = Array.isArray(T.tool_calls) ? T.tool_calls : [], gt = T.observation && Array.isArray(T.observation.results) ? T.observation.results : [], ve = ct(T.message);
			if (ve.length > 0) {
				const D = ft(E, B, Ue(T.source), ve, Ms, .6, Object.assign({}, mt, { kind: "message" }), {
					turnIndex: ut,
					model: Q,
					tokenUsage: Es
				});
				r.push(D);
			}
			if (typeof T.reasoning_content == "string" && T.reasoning_content.length > 0) {
				const D = ft(E, B, "reasoning", T.reasoning_content, 0, .4, Object.assign({}, mt, { kind: "reasoning" }), {
					turnIndex: ut,
					model: Q
				});
				r.push(D);
			}
			const Ee = /* @__PURE__ */ new Map();
			for (let D = 0; D < gt.length; D += 1) {
				const S = gt[D];
				typeof S.source_call_id == "string" && Ee.set(S.source_call_id, ct(S.content));
			}
			for (let D = 0; D < Ce.length; D += 1) {
				const S = Ce[D];
				let W = "";
				if (S.arguments != null) if (typeof S.arguments == "string") W = S.arguments;
				else try {
					W = JSON.stringify(S.arguments);
				} catch {
					W = "[unserializable arguments]";
				}
				const Wt = (S.function_name || "tool") + (W ? ": " + W : ""), yt = typeof S.tool_call_id == "string" ? Ee.get(S.tool_call_id) : void 0, Pt = ft(E, B, "tool_call", Wt, qt(S), .7, Object.assign({}, mt, {
					kind: "tool_call",
					toolCall: S
				}), {
					turnIndex: ut,
					model: Q,
					toolName: S.function_name,
					toolInput: S.arguments,
					toolCallId: typeof S.tool_call_id == "string" ? S.tool_call_id : null,
					toolOutput: yt ?? null
				});
				r.push(Pt);
			}
			for (let D = 0; D < gt.length; D += 1) {
				const S = gt[D], W = ct(S.content), Wt = Oe.test(W) || De.test(W), yt = Array.isArray(S.subagent_trajectory_ref) ? S.subagent_trajectory_ref : null, Pt = ft(E, B, "context", W, qt(S), .5, Object.assign({}, mt, {
					kind: "observation",
					result: S,
					subagentTrajectoryRefs: yt
				}), {
					turnIndex: ut,
					model: Q,
					parentToolCallId: typeof S.source_call_id == "string" ? S.source_call_id : null,
					isError: Wt
				});
				r.push(Pt);
			}
		}
		g && l.push(g), l.length === 0 && l.push({
			index: 0,
			startTime: r.length > 0 ? r[0].t : 0,
			endTime: 0,
			eventIndices: [],
			userMessage: "(no user message)",
			toolCount: 0,
			hasError: !1
		});
		for (let C = 0; C < r.length; C += 1) {
			const T = r[C];
			let E = null;
			const O = typeof T.turnIndex == "number" ? T.turnIndex : -1;
			if (O >= 0 && O < l.length) E = l[O];
			else for (let J = l.length - 1; J >= 0; J -= 1) if (T.t >= l[J].startTime) {
				E = l[J];
				break;
			}
			E || (E = l[0]), T.turnIndex = E.index, E.eventIndices.push(C);
			const H = T.raw, B = !!(H && H.isCopiedContext);
			T.track === "tool_call" && !B && (E.toolCount = (E.toolCount || 0) + 1), T.isError && (E.hasError = !0), E.endTime < T.t + T.duration && (E.endTime = T.t + T.duration);
		}
		const c = {};
		let x = 0, d = 0;
		for (let C = 0; C < r.length; C += 1) {
			const T = r[C], E = T.raw, O = !!(E && E.isCopiedContext);
			T.track === "tool_call" && !O && (x += 1), T.isError && (d += 1), T.model && (c[T.model] = (c[T.model] || 0) + 1);
		}
		const m = n.final_metrics;
		let u = 0, p = 0, f = 0;
		for (let C = 0; C < o.length; C += 1) {
			const T = o[C].metrics;
			T && (u += T.prompt_tokens || 0, p += T.completion_tokens || 0, f += T.cached_tokens || 0);
		}
		let h = 0, y = 0, k = 0, b = 0, I = !1;
		if (m ? (h = typeof m.total_prompt_tokens == "number" ? m.total_prompt_tokens : u, y = typeof m.total_completion_tokens == "number" ? m.total_completion_tokens : p, k = typeof m.total_cached_tokens == "number" ? m.total_cached_tokens : f, typeof m.total_cost_usd == "number" && (b = m.total_cost_usd, I = !0)) : (h = u, y = p, k = f), !I) for (let C = 0; C < o.length; C += 1) {
			const T = o[C].metrics;
			T && typeof T.cost_usd == "number" && (b += T.cost_usd, I = !0);
		}
		const v = {};
		for (let C = 0; C < o.length; C += 1) {
			const T = o[C], E = T.metrics;
			if (!E) continue;
			const O = T.model_name || n.agent.model_name;
			if (!O) continue;
			const H = E.prompt_tokens || 0, B = E.completion_tokens || 0, J = E.cached_tokens || 0;
			H + B + J !== 0 && (v[O] || (v[O] = {
				inputTokens: 0,
				outputTokens: 0,
				cacheRead: 0,
				cacheWrite: 0
			}), v[O].inputTokens += H, v[O].outputTokens += B, v[O].cacheRead += J);
		}
		for (const C of Object.keys(v)) {
			const T = v[C];
			T.cacheHitRate = V(T.inputTokens, T.cacheWrite, T.cacheRead);
		}
		const P = Object.keys(v).length > 0, N = h + y + k > 0 ? {
			inputTokens: h,
			outputTokens: y,
			cacheRead: k,
			cacheWrite: 0,
			cacheHitRate: V(h, 0, k)
		} : null, L = et(r), U = n.agent.model_name || (Object.keys(c)[0] ?? null), w = {
			totalEvents: r.length,
			totalTurns: l.length,
			totalToolCalls: x,
			errorCount: d,
			duration: L,
			models: c,
			primaryModel: U || null,
			tokenUsage: N,
			warnings: s,
			parseIssues: {
				malformedLines: 0,
				invalidEvents: 0
			},
			format: "atif",
			sessionId: n.session_id || null,
			schemaVersion: n.schema_version || null,
			agent: n.agent,
			continuationRef: n.continued_trajectory_ref || null
		};
		return P && (w.modelTokenUsage = v), I && (w.totalCost = b), typeof n.notes == "string" && n.notes.length > 0 && (w.customTitle = n.notes), {
			events: r,
			turns: l,
			metadata: w
		};
	}
	const He = 4e3, Ve = 8;
	function A(t) {
		return !!(t && typeof t == "object" && !Array.isArray(t));
	}
	function xt(t) {
		if (!t) return null;
		const e = new Date(t);
		return Number.isNaN(e.getTime()) ? null : e.getTime() / 1e3;
	}
	function $e(t) {
		const e = [];
		let n = 0;
		const s = t.split(/\r?\n/);
		for (let o = 0; o < s.length; o += 1) {
			const i = s[o].trim();
			if (i) try {
				e.push(JSON.parse(i));
			} catch {
				n += 1;
			}
		}
		return {
			records: e,
			malformedLines: n
		};
	}
	function Xe(t, e) {
		const n = [], s = t.split(/\r?\n/);
		for (let o = 0; o < s.length && n.length < e; o += 1) {
			const i = s[o].trim();
			if (i) try {
				n.push(JSON.parse(i));
			} catch {
				continue;
			}
		}
		return n;
	}
	function Ke(t) {
		if (t.type !== "session_meta" || !A(t.payload)) return !1;
		const e = t.payload;
		return typeof e.originator == "string" && e.originator.startsWith("codex-") || typeof e.thread_source == "string" || typeof e.model_provider == "string" && typeof e.source == "string";
	}
	function Ge(t) {
		if (t.type !== "turn_context" || !A(t.payload)) return !1;
		const e = t.payload;
		return typeof e.turn_id == "string" && (typeof e.model == "string" || typeof e.approval_policy == "string" || typeof e.sandbox_policy == "string");
	}
	function Qe(t) {
		if (t.type !== "response_item" || !A(t.payload)) return !1;
		const e = t.payload.type;
		return e === "message" || e === "reasoning" || e === "function_call" || e === "function_call_output" || e === "custom_tool_call" || e === "custom_tool_call_output" || e === "web_search_call";
	}
	function Ye(t) {
		return Ft(Xe(t, Ve));
	}
	function Ft(t) {
		if (t.some(Ke)) return !0;
		let e = 0;
		for (let n = 0; n < t.length; n += 1) Ge(t[n]) && (e += 1), Qe(t[n]) && (e += 1);
		return e >= 2;
	}
	function dt(t) {
		if (typeof t == "number" && Number.isFinite(t)) return Math.max(0, t);
		if (typeof t == "string" && t.trim()) {
			const e = Number(t);
			if (Number.isFinite(e)) return Math.max(0, e);
		}
		return 0;
	}
	function Tt(t) {
		if (typeof t == "string") return t;
		if (Array.isArray(t)) return t.map(function(e) {
			return typeof e == "string" ? e : A(e) ? typeof e.text == "string" ? e.text : typeof e.content == "string" ? e.content : typeof e.output_text == "string" ? e.output_text : "" : "";
		}).filter(Boolean).join(`
`);
		if (A(t)) {
			if (typeof t.text == "string") return t.text;
			if (typeof t.content == "string") return t.content;
		}
		return "";
	}
	function zt(t) {
		if (typeof t != "string") return t;
		const e = t.trim();
		if (!e) return t;
		try {
			return JSON.parse(e);
		} catch {
			return t;
		}
	}
	function Ze(t) {
		const e = zt(t);
		if (typeof e == "string") return M(e, 120);
		if (!A(e)) return e == null ? "" : M(String(e), 120);
		if (typeof e.command == "string") return M(e.command, 120);
		if (typeof e.query == "string") return M(e.query, 120);
		if (typeof e.path == "string") return e.path;
		const n = Object.keys(e);
		return n.length === 0 ? "" : n.slice(0, 3).map(function(s) {
			const o = e[s];
			return typeof o == "string" ? s + "=" + M(o, 40) : s;
		}).join(", ") + (n.length > 3 ? ", +" + (n.length - 3) + " more" : "");
	}
	function tn(t, e) {
		return t.success === !1 || t.status === "failed" || t.status === "error" || t.error || t.stderr ? !0 : /\b(error|failed|exception|traceback|fatal|panic)\b/i.test(e) || /exit (code|status) [1-9]/i.test(e);
	}
	function kt(t) {
		if (Array.isArray(t)) return t.map(kt);
		if (!A(t)) return t;
		const e = {};
		return Object.keys(t).forEach(function(n) {
			n === "encrypted_content" || n === "base_instructions" ? e[n] = "[elided]" : e[n] = kt(t[n]);
		}), e;
	}
	function nt(t, e, n, s, o, i, a, r) {
		const l = {
			t,
			agent: e,
			track: n,
			text: M(s, He),
			duration: o,
			intensity: i,
			raw: kt(a),
			turnIndex: 0,
			isError: !1
		};
		return r && Object.assign(l, r), l;
	}
	function Bt(t, e) {
		return xt(t.timestamp) ?? e;
	}
	function _t(t, e, n) {
		return t.turns[e] || (t.turns[e] = {
			id: e,
			startTime: n,
			endTime: null,
			userMessage: null
		}, t.turnCount !== void 0 && t.turnCount++), t.turns[e];
	}
	function st(t) {
		return t.currentTurnId;
	}
	function ot(t) {
		const e = st(t);
		return e && t.turnContexts[e] && t.turnContexts[e].model ? t.turnContexts[e].model || null : t.currentModel;
	}
	function Jt(t, e, n, s) {
		const o = e.payload || {}, i = o.role;
		if (i === "developer") return;
		const a = Tt(o.content).trim();
		if (!a) return;
		const r = st(n);
		if (i === "user") {
			r && (_t(n, r, s).userMessage = a), t.push(nt(s, "user", "output", a, .5, .8, e, {
				codexTurnId: r,
				model: ot(n)
			}));
			return;
		}
		i === "assistant" && t.push(nt(s, "assistant", "output", a, .7, .7, e, {
			codexTurnId: r,
			model: ot(n)
		}));
	}
	function Ht(t, e, n, s) {
		const o = e.payload || {}, i = Tt(o.summary || o.content).trim();
		i && t.push(nt(s, "assistant", "reasoning", i, .4, .5, e, {
			codexTurnId: st(n),
			model: ot(n)
		}));
	}
	function Vt(t, e, n, s) {
		const o = e.payload || {}, i = o.type, a = i === "web_search_call" ? "web_search" : String(o.name || i || "tool"), r = i === "custom_tool_call" ? o.input : i === "web_search_call" ? o.action || {} : zt(o.arguments || {}), l = Ze(r);
		t.push(nt(s, "assistant", "tool_call", a + (l ? ": " + l : ""), .5, .8, e, {
			toolName: a,
			toolInput: r,
			toolCallId: o.call_id || null,
			codexTurnId: st(n),
			model: ot(n)
		}));
	}
	function en(t) {
		return typeof t.output == "string" ? t.output : t.stdout || t.stderr ? [t.stdout, t.stderr].filter(Boolean).join(`
`) : t.query ? "Web search completed: " + t.query : t.changes ? "Patch applied" : Tt(t.output || t.content || t.message);
	}
	function It(t, e, n, s) {
		const o = e.payload || {}, i = en(o), a = tn(o, i);
		t.push(nt(s, "assistant", "context", "Result: " + M(i || "completed", 300), .3, a ? 1 : .5, e, {
			isError: a,
			toolCallId: o.call_id || null,
			codexTurnId: st(n),
			model: ot(n)
		}));
	}
	function bt(t) {
		return typeof t.query == "string" ? t.query : A(t.action) && typeof t.action.query == "string" ? t.action.query : "";
	}
	function nn(t, e) {
		if (!e.call_id) return;
		const n = bt(e);
		for (let s = t.length - 1; s >= 0; s -= 1) {
			const o = t[s];
			if (o.track !== "tool_call" || o.toolName !== "web_search") continue;
			if (o.toolCallId) return;
			const i = o.raw, a = i && A(i.payload) ? i.payload : {};
			if (!(n && bt(a) !== n)) {
				o.toolCallId = e.call_id;
				return;
			}
		}
	}
	function $t(t, e) {
		const n = t.payload || {}, s = typeof n.turn_id == "string" ? n.turn_id : null;
		s && (e.currentTurnId = s, e.currentModel = typeof n.model == "string" ? n.model : e.currentModel, e.turnContexts[s] = {
			turnId: s,
			model: typeof n.model == "string" ? n.model : null,
			effort: typeof n.effort == "string" ? n.effort : null,
			cwd: typeof n.cwd == "string" ? n.cwd : null,
			summary: typeof n.summary == "string" ? n.summary : null
		});
	}
	function Xt(t, e, n, s) {
		const o = t.payload || {}, i = o.type;
		if (i === "task_started") {
			const a = typeof o.turn_id == "string" ? o.turn_id : "turn-" + (e.turnCount ?? Object.keys(e.turns).length);
			e.currentTurnId = a, _t(e, a, xt(o.started_at) || s);
			return;
		}
		if (i === "task_complete") {
			const a = typeof o.turn_id == "string" ? o.turn_id : e.currentTurnId;
			if (a) {
				const r = _t(e, a, s);
				r.endTime = xt(o.completed_at) || s + dt(o.duration_ms) / 1e3;
			}
			return;
		}
		(i === "patch_apply_end" || i === "web_search_end") && (i === "web_search_end" && nn(n, o), It(n, t, e, s));
	}
	function sn(t, e) {
		const n = [];
		let s = 0;
		for (let o = 0; o < t.length; o += 1) {
			const i = t[o], a = Bt(i, s);
			if (s += 1, i.type === "turn_context") {
				$t(i, e);
				continue;
			}
			if (i.type === "event_msg") {
				Xt(i, e, n, a);
				continue;
			}
			if (i.type !== "response_item" || !A(i.payload)) continue;
			const r = i.payload.type;
			r === "message" ? Jt(n, i, e, a) : r === "reasoning" ? Ht(n, i, e, a) : r === "function_call" || r === "custom_tool_call" || r === "web_search_call" ? Vt(n, i, e, a) : (r === "function_call_output" || r === "custom_tool_call_output") && It(n, i, e, a);
		}
		return n.sort(function(o, i) {
			return o.t - i.t;
		}), n;
	}
	function on(t) {
		if (t.length === 0) return;
		let e = t[0].t;
		for (let n = 1; n < t.length; n += 1) t[n].t < e && (e = t[n].t);
		for (let n = 0; n < t.length; n += 1) t[n].t = Math.max(0, t[n].t - e);
	}
	function rn(t, e) {
		Object.keys(t.turns).forEach(function(n) {
			const s = t.turns[n];
			s.startTime = Math.max(0, s.startTime - e), s.endTime != null && (s.endTime = Math.max(s.startTime, s.endTime - e));
		});
	}
	function an(t, e) {
		const n = Object.keys(e.turns).map(function(i) {
			const a = e.turns[i], r = e.turnContexts[i];
			return {
				index: 0,
				startTime: a.startTime,
				endTime: a.endTime == null ? a.startTime : a.endTime,
				eventIndices: [],
				userMessage: a.userMessage || r && r.summary || "(continuation)",
				toolCount: 0,
				hasError: !1,
				turnId: i,
				model: r && r.model || null,
				effort: r && r.effort || null
			};
		}).sort(function(i, a) {
			return i.startTime - a.startTime;
		});
		if (n.length === 0) {
			let i = null;
			for (let a = 0; a < t.length; a += 1) {
				const r = t[a];
				!i || r.agent === "user" ? (i && n.push(i), i = {
					index: n.length,
					startTime: r.t,
					endTime: r.t + r.duration,
					eventIndices: [a],
					userMessage: r.agent === "user" ? r.text : "(system)",
					toolCount: r.track === "tool_call" ? 1 : 0,
					hasError: r.isError
				}) : (i.eventIndices.push(a), i.endTime = Math.max(i.endTime, r.t + r.duration), r.track === "tool_call" && (i.toolCount = (i.toolCount || 0) + 1), r.isError && (i.hasError = !0)), r.turnIndex = i.index;
			}
			return i && n.push(i), n;
		}
		for (let i = 0; i < n.length; i += 1) n[i].index = i;
		const s = {};
		n.forEach(function(i) {
			i.turnId && (s[String(i.turnId)] = i);
		});
		for (let i = 0; i < t.length; i += 1) {
			const a = t[i], r = typeof a.codexTurnId == "string" ? a.codexTurnId : null;
			let l = r ? s[r] : null;
			if (!l) {
				for (let g = n.length - 1; g >= 0; g -= 1) if (a.t >= n[g].startTime) {
					l = n[g];
					break;
				}
			}
			l || (l = n[0]), a.turnIndex = l.index, l.eventIndices.push(i), l.endTime = Math.max(l.endTime, a.t + a.duration), a.agent === "user" && (!l.userMessage || l.userMessage === "(continuation)") && (l.userMessage = a.text), a.track === "tool_call" && (l.toolCount = (l.toolCount || 0) + 1), a.isError && (l.hasError = !0);
		}
		const o = n.filter(function(i) {
			return i.eventIndices.length > 0;
		});
		for (let i = 0; i < o.length; i += 1) {
			const a = o[i];
			a.index = i, a.eventIndices.forEach(function(r) {
				t[r].turnIndex = i;
			});
		}
		return o;
	}
	function ln(t) {
		const e = t.find(function(n) {
			return n.type === "session_meta" && A(n.payload);
		});
		return e && e.payload || {};
	}
	function un(t) {
		if (typeof t.agent_nickname == "string") return t.agent_nickname;
		if (typeof t.agent_role == "string") return t.agent_role;
		if (!A(t.source)) return null;
		if (typeof t.source.subagent == "string") return t.source.subagent;
		if (!A(t.source.subagent)) return null;
		const e = t.source.subagent;
		if (typeof e.name == "string") return e.name;
		if (typeof e.other == "string") return e.other;
		if (A(e.thread_spawn)) {
			const s = e.thread_spawn;
			if (typeof s.agent_nickname == "string") return s.agent_nickname;
			if (typeof s.agent_role == "string") return s.agent_role;
			if (typeof s.name == "string") return s.name;
		}
		const n = Object.keys(e);
		return n.length === 1 && n[0] !== "thread_spawn" ? n[0] : null;
	}
	function cn(t, e) {
		let n = null;
		for (let a = 0; a < t.length; a += 1) {
			const r = t[a].payload;
			if (t[a].type !== "event_msg" || !A(r) || r.type !== "token_count") continue;
			const l = A(r.info) ? r.info : {};
			A(l.total_token_usage) && (n = l.total_token_usage);
		}
		if (!n) return null;
		const s = dt(n.input_tokens), o = dt(n.cached_input_tokens), i = {
			inputTokens: s,
			outputTokens: dt(n.output_tokens),
			cacheRead: o,
			cacheWrite: 0,
			cacheHitRate: V(s, 0, o)
		};
		return i.inputTokens + i.outputTokens + i.cacheRead === 0 ? null : (e.push("Codex token usage is based on cumulative token_count totals"), i);
	}
	function Kt(t, e, n, s, o, i, a) {
		const r = ln(t), l = cn(t, i), g = { ...a?.models };
		Object.keys(s.turnContexts).forEach(function(c) {
			const x = s.turnContexts[c].model;
			x && (g[x] = (g[x] || 0) + 1);
		}), e.forEach(function(c) {
			c.model && (g[c.model] = (g[c.model] || 0) + 1);
		});
		const _ = Object.entries(g).sort(function(c, x) {
			return x[1] - c[1];
		});
		return o > 0 && i.push(o + " malformed line(s) skipped"), {
			totalEvents: e.length,
			totalTurns: n.length,
			totalToolCalls: a?.totalToolCalls ?? e.filter(function(c) {
				return c.track === "tool_call";
			}).length,
			errorCount: a?.errorCount ?? e.filter(function(c) {
				return c.isError;
			}).length,
			duration: a?.duration ?? et(e),
			models: g,
			primaryModel: _.length > 0 ? _[0][0] : null,
			tokenUsage: l,
			warnings: i,
			parseIssues: {
				malformedLines: o,
				invalidEvents: 0
			},
			format: "codex",
			sessionId: typeof r.id == "string" ? r.id : null,
			cwd: typeof r.cwd == "string" ? r.cwd : null,
			originator: typeof r.originator == "string" ? r.originator : null,
			cliVersion: typeof r.cli_version == "string" ? r.cli_version : null,
			modelProvider: typeof r.model_provider == "string" ? r.model_provider : null,
			threadSource: typeof r.thread_source == "string" ? r.thread_source : null,
			parentThreadId: typeof r.parent_thread_id == "string" ? r.parent_thread_id : null,
			subagentName: un(r)
		};
	}
	function fn(t, e = 0) {
		if (t.length === 0) return null;
		const n = [], s = {
			currentTurnId: null,
			currentModel: null,
			turnContexts: {},
			turns: {}
		}, o = sn(t, s);
		if (o.length === 0) return null;
		const i = o.reduce(function(r, l) {
			return Math.min(r, l.t);
		}, o[0].t);
		on(o), rn(s, i);
		const a = an(o, s);
		return {
			events: o,
			turns: a,
			metadata: Kt(t, o, a, s, e, n)
		};
	}
	function dn(t) {
		const e = $e(t);
		return fn(e.records, e.malformedLines);
	}
	const q = {
		getEventTime: Bt,
		updateTurnContext: $t,
		handleEventMessage: Xt,
		pushMessageEvent: Jt,
		pushReasoningEvent: Ht,
		pushToolCallEvent: Vt,
		pushToolOutputEvent: It,
		getWebSearchQuery: bt,
		buildMetadata: Kt,
		isRecord: A
	};
	var hn = 1e9, Ct = [
		{
			match: "gpt-5.4-nano",
			input: .2,
			cachedInput: .02,
			output: 1.25
		},
		{
			match: "gpt-5.4-mini",
			input: .75,
			cachedInput: .075,
			output: 4.5
		},
		{
			match: "gpt-5-mini",
			input: .25,
			cachedInput: .025,
			output: 2
		},
		{
			match: "gpt-5.3-codex",
			input: 1.75,
			cachedInput: .175,
			output: 14
		},
		{
			match: "gpt-5.5",
			input: 5,
			cachedInput: .5,
			output: 30,
			longContext: {
				threshold: 272e3,
				input: 10,
				cachedInput: 1,
				output: 45
			}
		},
		{
			match: "gpt-5.4",
			input: 2.5,
			cachedInput: .25,
			output: 15,
			longContext: {
				threshold: 272e3,
				input: 5,
				cachedInput: .5,
				output: 22.5
			}
		},
		{
			match: "gpt-5",
			input: 1.25,
			cachedInput: .125,
			output: 10
		},
		{
			match: "gpt-4.1",
			input: 2,
			cachedInput: .2,
			output: 8
		},
		{
			match: "gpt-4o-mini",
			input: .15,
			cachedInput: .015,
			output: .6
		},
		{
			match: "gpt-4o",
			input: 2.5,
			cachedInput: .25,
			output: 10
		},
		{
			match: "o4-mini",
			input: 1.1,
			output: 4.4
		},
		{
			match: "o3-mini",
			input: 1.1,
			output: 4.4
		},
		{
			match: "o3",
			input: 10,
			output: 40
		},
		{
			match: "claude-haiku-4",
			input: 1,
			cachedInput: .1,
			cacheWrite: 1.25,
			output: 5
		},
		{
			match: "claude-opus-4",
			input: 5,
			cachedInput: .5,
			cacheWrite: 6.25,
			output: 25
		},
		{
			match: "claude-sonnet-4",
			input: 3,
			cachedInput: .3,
			cacheWrite: 3.75,
			output: 15
		},
		{
			match: "claude-fable-5",
			input: 10,
			cachedInput: 1,
			cacheWrite: 12.5,
			output: 50
		},
		{
			match: "claude-3-5-sonnet",
			input: 3,
			cachedInput: .3,
			cacheWrite: 3.75,
			output: 15
		},
		{
			match: "claude-3-5-haiku",
			input: .8,
			cachedInput: .08,
			cacheWrite: 1,
			output: 4
		},
		{
			match: "claude-3-opus",
			input: 15,
			cachedInput: 1.5,
			cacheWrite: 18.75,
			output: 75
		},
		{
			match: "claude-3-sonnet",
			input: 3,
			cachedInput: .3,
			cacheWrite: 3.75,
			output: 15
		},
		{
			match: "claude-3-haiku",
			input: .25,
			cachedInput: .03,
			cacheWrite: .31,
			output: 1.25
		},
		{
			match: "gemini-2.5-pro",
			input: 1.25,
			cachedInput: .125,
			output: 10
		},
		{
			match: "gemini-3-flash",
			input: .5,
			cachedInput: .05,
			output: 3
		},
		{
			match: "gemini-3.5-flash",
			input: 1.5,
			cachedInput: .15,
			output: 9
		},
		{
			match: "gemini-3.1-pro",
			input: 2,
			cachedInput: .2,
			output: 12,
			longContext: {
				threshold: 2e5,
				input: 4,
				cachedInput: .4,
				output: 18
			}
		},
		{
			match: "raptor-mini",
			input: .25,
			cachedInput: .025,
			output: 2
		},
		{
			match: "mai-code-1-flash",
			input: .75,
			cachedInput: .075,
			output: 4.5
		}
	], pn = {
		input: 3,
		cachedInput: .3,
		cacheWrite: 3.75,
		output: 15
	};
	function mn(t) {
		return String(t).toLowerCase().replace(/[^a-z0-9.]+/g, "-");
	}
	function gn(t) {
		if (!t) return null;
		for (var e = mn(t), n = 0; n < Ct.length; n++) if (e.includes(Ct[n].match)) return Ct[n];
		return e.includes("claude") ? pn : null;
	}
	function yn(t, e) {
		return t.longContext && (e || 0) > t.longContext.threshold ? t.longContext : t;
	}
	function xn(t) {
		return t.cachedInput != null ? t.cachedInput : t.input * .1;
	}
	function Tn(t) {
		return t.cacheWrite != null ? t.cacheWrite : t.input;
	}
	function kn(t, e) {
		if (!t) return 0;
		var n = gn(e);
		if (!n) return 0;
		var s = yn(n, t.inputTokens || 0), o = Math.max((t.inputTokens || 0) - (t.cacheRead || 0) - (t.cacheWrite || 0), 0) / 1e6 * s.input, i = (t.outputTokens || 0) / 1e6 * s.output, a = (t.cacheRead || 0) / 1e6 * xn(s), r = (t.cacheWrite || 0) / 1e6 * Tn(s);
		return o + i + a + r;
	}
	function _n(t) {
		if (!t) return 0;
		for (var e = 0, n = Object.keys(t), s = 0; s < n.length; s++) e += kn(t[n[s]], n[s]);
		return e;
	}
	function Gt(t) {
		return t == null || !Number.isFinite(t) ? null : t / hn;
	}
	const In = 4e3;
	function $(t) {
		if (!t) return null;
		const e = new Date(t);
		return Number.isNaN(e.getTime()) ? null : e.getTime() / 1e3;
	}
	function bn(t) {
		const e = t.split(`
`), n = [];
		let s = 0;
		for (let o = 0; o < e.length; o += 1) {
			const i = e[o].trim();
			if (i) try {
				n.push(JSON.parse(i));
			} catch {
				s += 1;
			}
		}
		return {
			records: n,
			malformedLines: s
		};
	}
	function Cn(t) {
		const e = {};
		for (let n = 0; n < t.length; n += 1) {
			const s = t[n];
			s.type === "tool.execution_complete" && s.data && s.data.toolCallId && (e[s.data.toolCallId] = s);
		}
		return { completes: e };
	}
	function R(t, e, n, s, o, i, a, r) {
		const l = {
			t,
			agent: e,
			track: n,
			text: M(s, In),
			duration: o,
			intensity: i,
			raw: a,
			turnIndex: 0,
			isError: !1
		};
		return r && Object.assign(l, r), l;
	}
	function X(t, e = "reasoningEffort") {
		const n = t && t[e];
		return typeof n == "string" && n.trim() ? n.trim() : null;
	}
	function vn(t) {
		const e = [];
		function n(s) {
			s && !e.includes(s) && e.push(s);
		}
		for (let s = 0; s < t.length; s += 1) {
			const o = t[s], i = o.data || {};
			o.type === "session.start" || o.type === "session.resume" ? n(X(i)) : o.type === "session.model_change" && (n(X(i, "previousReasoningEffort")), n(X(i)));
		}
		return e;
	}
	function En(t) {
		let e = null;
		for (let n = 0; n < t.length; n += 1) {
			const s = t[n];
			(s.type === "session.start" || s.type === "session.resume" || s.type === "session.model_change") && (e = X(s.data) || e);
		}
		return e;
	}
	function Mn(t, e, n) {
		const s = [];
		let o = !1;
		for (let r = 0; r < e.length; r += 1) {
			const l = e[r], g = l.data || {};
			if (l.type === "session.start" || l.type === "session.resume") {
				const d = X(g), m = $(l.type === "session.start" ? g.startTime || l.timestamp : g.resumeTime || l.timestamp);
				d && m !== null && (s.push({
					t: Math.max(m - n, 0),
					effort: d
				}), o = !0);
				continue;
			}
			if (l.type !== "session.model_change") continue;
			const _ = X(g, "previousReasoningEffort");
			!o && _ && (s.push({
				t: 0,
				effort: _
			}), o = !0);
			const c = X(g), x = $(l.timestamp);
			c && x !== null && s.push({
				t: Math.max(x - n, 0),
				effort: c
			});
		}
		s.sort(function(r, l) {
			return r.t - l.t;
		});
		let i = null, a = 0;
		for (let r = 0; r < t.length; r += 1) {
			const l = t[r];
			for (; a < s.length && s[a].t <= l.t;) i = s[a].effort, a += 1;
			i && (l.reasoningEffort = i);
		}
	}
	function Qt(t, e, n, s) {
		const o = [], i = {}, a = s?.taskToolMap || {}, r = s?.subagentStartTimes || {}, l = s?.subagentLifecycle || {};
		for (let _ = 0; !s && _ < t.length; _ += 1) {
			const c = t[_], x = c.data || {};
			if (c.type === "tool.execution_start" && x.toolName === "task") {
				const d = x.arguments || {};
				a[x.toolCallId] = {
					agentType: d.agent_type || "task",
					description: d.description || d.name || ""
				};
			}
			if (c.type === "subagent.started") {
				if (x.toolCallId) {
					const d = $(c.timestamp);
					d !== null && (r[x.toolCallId] = d);
				}
				l[x.toolCallId || ""] = {
					agentName: x.agentName || x.agentType,
					agentDisplayName: x.agentDisplayName || x.agentName
				};
			}
		}
		function g(_, c) {
			const x = _ ? l[_] : null, d = _ ? a[_] : null;
			return {
				agentName: x && x.agentName || d && d.agentType || c.agentName || null,
				agentDisplayName: x && x.agentDisplayName || c.agentDisplayName || c.agentName || d && d.description || null
			};
		}
		for (let _ = 0; _ < t.length; _ += 1) {
			const c = t[_], x = $(c.timestamp);
			if (x === null) continue;
			let d = x - e;
			d < 0 && (d = 0);
			const m = c.type, u = c.data || {};
			if (m === "user.message") {
				o.push(R(d, "user", "output", u.content || "", .5, .9, c));
				continue;
			}
			if (m === "assistant.message") {
				Sn(o, d, u, c, n, u.parentToolCallId || null, g(u.parentToolCallId, u));
				continue;
			}
			if (m === "assistant.reasoning") {
				if (u.content && u.content.trim()) {
					const p = u.parentToolCallId ? g(u.parentToolCallId, u) : {
						agentName: null,
						agentDisplayName: null
					};
					o.push(R(d, "assistant", "reasoning", u.content, .3, .5, c, {
						parentToolCallId: u.parentToolCallId || null,
						agentName: p.agentName,
						agentDisplayName: p.agentDisplayName
					}));
				}
				continue;
			}
			if (m === "tool.execution_start") {
				const p = typeof u.toolCallId == "string" && u.toolCallId.length > 0 ? u.toolCallId : null;
				if (p) {
					if (i[p]) continue;
					i[p] = !0;
				}
				Nn(o, d, x, u, c, n, a, l);
				continue;
			}
			if (m === "subagent.started") {
				const p = g(u.toolCallId, u), f = p.agentDisplayName || "Sub-agent";
				o.push(R(d, "system", "agent", f + " started", .3, .5, c, {
					toolCallId: u.toolCallId || null,
					agentName: p.agentName || "task",
					agentDisplayName: f
				}));
				continue;
			}
			if (m === "subagent.completed") {
				const p = g(u.toolCallId, u), f = p.agentDisplayName || "Sub-agent", h = u.toolCallId ? r[u.toolCallId] : void 0, y = h ? Math.max(x - h, .1) : .5;
				o.push(R(d, "system", "agent", f + " completed", y, .4, c, {
					toolCallId: u.toolCallId || null,
					agentName: p.agentName || "task",
					agentDisplayName: f
				}));
				continue;
			}
			if (m === "subagent.failed") {
				const p = g(u.toolCallId, u), f = p.agentDisplayName || "Sub-agent", h = u.toolCallId ? r[u.toolCallId] : void 0, y = h ? Math.max(x - h, .1) : .5;
				let k = f + " failed";
				u.error && (k += ": " + M(u.error, 200)), o.push(R(d, "system", "agent", k, y, .8, c, {
					isError: !0,
					toolCallId: u.toolCallId || null,
					agentName: p.agentName || "task",
					agentDisplayName: f
				}));
				continue;
			}
			if (m === "system.message") {
				u.content && o.push(R(d, "system", "context", u.content, .3, .3, c));
				continue;
			}
			if (m === "system.notification") {
				u.content && o.push(R(d, "system", "context", u.content, .2, .2, c));
				continue;
			}
			if (m === "session.error") {
				const p = u.message || u.errorType || "Session error";
				o.push(R(d, "system", "context", p, .5, 1, c, { isError: !0 }));
				continue;
			}
			if (m === "session.model_change") {
				const p = ["Model: " + (u.previousModel || "?") + " → " + (u.newModel || "?")], f = X(u, "previousReasoningEffort"), h = X(u);
				(f || h) && p.push("Reasoning effort: " + (f || "?") + " → " + (h || "?"));
				const y = p.join(" | ");
				o.push(R(d, "system", "context", y, .2, .3, c));
				continue;
			}
			if (m === "session.mode_changed") {
				const p = "Mode: " + (u.previousMode || "?") + " → " + (u.newMode || "?");
				o.push(R(d, "system", "context", p, .2, .3, c));
				continue;
			}
			if (m === "session.compaction_complete") {
				let p = "Context compacted";
				u.tokensRemoved && (p += " (" + u.tokensRemoved.toLocaleString() + " tokens removed)"), o.push(R(d, "system", "context", p, .3, .4, c));
				continue;
			}
			if (m === "session.truncation") {
				let p = "Context truncated";
				u.tokensRemoved && (p += " (" + u.tokensRemoved.toLocaleString() + " tokens removed)"), o.push(R(d, "system", "context", p, .3, .4, c));
				continue;
			}
			if (m === "session.task_complete") {
				o.push(R(d, "system", "output", u.summary || "Task completed", .3, .5, c));
				continue;
			}
			if (m === "session.info") {
				o.push(R(d, "system", "context", u.message || "Info", .2, .2, c));
				continue;
			}
			if (m === "session.warning") {
				o.push(R(d, "system", "context", u.message || "Warning", .2, .4, c));
				continue;
			}
		}
		return o.sort(function(_, c) {
			return _.t - c.t || 0;
		}), Mn(o, t, e), o;
	}
	function Sn(t, e, n, s, o, i, a) {
		const r = wn(n, o), l = {};
		i && (l.parentToolCallId = i), a.agentName && (l.agentName = a.agentName), a.agentDisplayName && (l.agentDisplayName = a.agentDisplayName), n.reasoningText && n.reasoningText.trim() && t.push(R(e, "assistant", "reasoning", n.reasoningText, .3, .5, s, Object.assign({
			model: r,
			tokenUsage: n.outputTokens ? { outputTokens: n.outputTokens } : null
		}, l)));
		const g = typeof n.content == "string" ? n.content.trim() : "";
		if (g && t.push(R(e + .01, "assistant", "output", g, .5, .7, s, Object.assign({ model: r }, l))), !g && (!n.reasoningText || !n.reasoningText.trim()) && Array.isArray(n.toolRequests) && n.toolRequests.length > 0) {
			const _ = n.toolRequests.map(function(c) {
				return c.name;
			}).join(", ");
			t.push(R(e, "assistant", "reasoning", "Invoking: " + _, .2, .3, s, Object.assign({ model: r }, l)));
		}
	}
	function Nn(t, e, n, s, o, i, a, r) {
		const l = i.completes[s.toolCallId], g = l ? $(l.timestamp) : null, _ = g ? Math.max(g - n, .1) : .5, c = l ? !!(l.data && l.data.success === !1) : !1;
		let x = "";
		l && l.data && l.data.result && (x = l.data.result.content || l.data.result.detailedContent || "");
		let d = s.toolName;
		if (s.arguments) {
			const k = Rn(s.arguments);
			k && (d += ": " + k);
		}
		if (c) {
			const k = l && l.data && l.data.error || x;
			k && (d += `
` + M(k, 200));
		}
		const m = a ? a[s.toolCallId] : null, u = s.parentToolCallId && a ? a[s.parentToolCallId] : null, p = r ? r[s.toolCallId] : null, f = s.parentToolCallId && r ? r[s.parentToolCallId] : null;
		let h = null, y = null;
		m ? (h = p && p.agentName || m.agentType, y = p && p.agentDisplayName || m.description || null) : (u || f) && (h = f && f.agentName || u && u.agentType || null, y = f && f.agentDisplayName || u && u.description || null), t.push(R(e, "assistant", "tool_call", d, _, c ? .9 : .6, o, {
			toolName: s.toolName,
			toolInput: s.arguments,
			toolOutput: x || null,
			toolCallId: s.toolCallId || null,
			isError: c,
			model: l && l.data && l.data.model || null,
			parentToolCallId: s.parentToolCallId || null,
			agentName: h,
			agentDisplayName: y
		}));
	}
	function wn(t, e) {
		if (Array.isArray(t.toolRequests)) for (let n = 0; n < t.toolRequests.length; n += 1) {
			const s = e.completes[t.toolRequests[n].toolCallId];
			if (s && s.data && s.data.model) return s.data.model;
		}
		return null;
	}
	function Rn(t) {
		if (!t) return "";
		if (t.command) return M(t.command, 120);
		if (t.pattern) return "'" + M(t.pattern, 60) + "'" + (t.path ? " in " + t.path : "");
		if (t.path) return t.path;
		if (t.query) return M(t.query, 120);
		if (t.prompt) return M(t.prompt, 120);
		if (t.intent) return t.intent;
		if (t.description) return M(t.description, 120);
		if (t.url) return M(t.url, 120);
		if (t.issue_number) return "#" + t.issue_number;
		if (t.pullNumber) return "PR #" + t.pullNumber;
		if (t.owner && t.repo) return t.owner + "/" + t.repo;
		const e = Object.keys(t);
		return e.length === 0 ? "" : e.length <= 3 ? e.map(function(n) {
			const s = t[n];
			return typeof s == "string" ? n + "=" + M(s, 40) : n;
		}).join(", ") : e.length + " args";
	}
	function An(t, e, n) {
		const s = [];
		let o = null, i = null;
		for (let a = 0; a < t.length; a += 1) {
			const r = t[a];
			if (r.type === "user.message" && (i = r.data && r.data.content || ""), r.type === "assistant.turn_start") {
				o && s.push(o);
				const l = $(r.timestamp);
				o = {
					index: s.length,
					startTime: l ? l - n : 0,
					endTime: 0,
					eventIndices: [],
					userMessage: i || "(continuation)",
					toolCount: 0,
					hasError: !1
				}, i = null;
			}
			if (r.type === "assistant.turn_end" && o) {
				const l = $(r.timestamp);
				l && (o.endTime = l - n);
			}
		}
		o && s.push(o);
		for (let a = 0; a < e.length; a += 1) {
			const r = e[a];
			let l = null;
			for (let g = s.length - 1; g >= 0; g -= 1) if (r.t >= s[g].startTime) {
				l = s[g];
				break;
			}
			!l && s.length > 0 && (l = s[0]), l && (r.turnIndex = l.index, l.eventIndices.push(a), r.track === "tool_call" && (l.toolCount = (l.toolCount || 0) + 1), r.isError && (l.hasError = !0), l.endTime < r.t + r.duration && (l.endTime = r.t + r.duration));
		}
		return s;
	}
	function ht(t, e) {
		const n = t && t[e];
		return n && typeof n.tokenCount == "number" ? n.tokenCount : 0;
	}
	function On(t) {
		if (!t) return null;
		if (t.usage) {
			const e = {
				inputTokens: t.usage.inputTokens || 0,
				outputTokens: t.usage.outputTokens || 0,
				cacheRead: t.usage.cacheReadTokens || 0,
				cacheWrite: t.usage.cacheWriteTokens || 0
			};
			if (e.inputTokens + e.outputTokens + e.cacheRead + e.cacheWrite > 0) return e;
		}
		if (t.tokenDetails) {
			const e = ht(t.tokenDetails, "input"), n = ht(t.tokenDetails, "cache_read"), s = ht(t.tokenDetails, "cache_write"), o = ht(t.tokenDetails, "output"), i = {
				inputTokens: e + n + s,
				outputTokens: o,
				cacheRead: n,
				cacheWrite: s
			};
			if (i.inputTokens + i.outputTokens + i.cacheRead + i.cacheWrite > 0) return i;
		}
		return null;
	}
	function Yt(t, e, n, s, o) {
		let i = null, a = null, r = null;
		for (let w = 0; w < t.length; w += 1) t[w].type === "session.start" && (i = t[w].data || null), t[w].type === "session.resume" && (a = t[w].data || null), t[w].type === "session.shutdown" && (r = t[w].data || null);
		const l = i || a;
		let g = o?.totalToolCalls || 0, _ = o?.errorCount || 0;
		const c = { ...o?.models };
		for (let w = 0; w < e.length; w += 1) e[w].track === "tool_call" && (g += 1), e[w].isError && (_ += 1), e[w].model && (c[e[w].model] = (c[e[w].model] || 0) + 1);
		let x = 0, d = 0, m = 0, u = 0, p = null, f = null, h = null, y = null;
		if (r && r.modelMetrics) {
			const w = r.modelMetrics;
			y = {};
			for (const C of Object.keys(w)) {
				const T = w[C], E = On(T), O = T && T.totalNanoAiu != null ? Gt(T.totalNanoAiu) : null;
				if (E) {
					const H = E.inputTokens, B = E.outputTokens, J = E.cacheRead, Q = E.cacheWrite;
					x += H, d += B, m += J, u += Q, y[C] = {
						inputTokens: H,
						outputTokens: B,
						cacheRead: J,
						cacheWrite: Q,
						cacheHitRate: V(H, Q, J),
						aiCredits: O
					};
				}
				O != null && (h = (h || 0) + O), c[C] || (c[C] = T.requests ? T.requests.count : 0);
			}
		}
		r && (r.totalNanoAiu != null && (h = Gt(r.totalNanoAiu)), h != null && (p = h, f = "ai_credits"));
		const k = Object.entries(c).sort(function(w, C) {
			return (C[1] || 0) - (w[1] || 0);
		}), b = k.length > 0 ? k[0][0] : null, I = vn(t), v = En(t), P = o?.duration ?? et(e), N = [];
		s > 0 && N.push(s + " malformed line(s) skipped"), r && r.shutdownType === "error" && N.push("Session ended with error: " + (r.errorReason || "unknown"));
		const L = l && l.context ? l.context : {}, U = V(x, u, m);
		return {
			totalEvents: e.length,
			totalTurns: n.length,
			totalToolCalls: g,
			errorCount: _,
			duration: P,
			models: c,
			primaryModel: b,
			reasoningEffort: v,
			reasoningEfforts: I,
			tokenUsage: x + d + m + u > 0 ? {
				inputTokens: x,
				outputTokens: d,
				cacheRead: m,
				cacheWrite: u,
				cacheHitRate: U
			} : null,
			warnings: N,
			parseIssues: {
				malformedLines: s,
				invalidEvents: 0
			},
			format: "copilot-cli",
			sessionId: l ? l.sessionId : null,
			producer: l ? l.producer : null,
			copilotVersion: l ? l.copilotVersion : null,
			selectedModel: l ? l.selectedModel : null,
			repository: L.repository || null,
			branch: L.branch || null,
			cwd: L.cwd || null,
			gitRoot: L.gitRoot || null,
			shutdownType: r ? r.shutdownType : null,
			codeChanges: r ? r.codeChanges : null,
			aiCredits: h,
			totalApiDurationMs: r ? r.totalApiDurationMs : null,
			totalCost: p,
			totalCostUnit: f,
			modelTokenUsage: y
		};
	}
	function Dn(t) {
		t = t.trimStart();
		const e = t.indexOf(`
`), n = e > 0 ? t.substring(0, e) : t;
		try {
			const s = JSON.parse(n.trim());
			return s.type === "session.start" || s.type === "session.resume" ? !!(s.data && (s.data.producer === "copilot-agent" || s.data.copilotVersion)) : !1;
		} catch {
			return !1;
		}
	}
	function jn(t, e) {
		if (t.length === 0) return null;
		let n = null;
		for (let a = 0; a < t.length; a += 1) {
			const r = t[a];
			if (r.type === "session.start" && r.data && r.data.startTime) {
				n = $(r.data.startTime);
				break;
			}
			if (r.type === "session.resume" && r.data && r.data.resumeTime) {
				n = $(r.data.resumeTime);
				break;
			}
		}
		n === null && (n = $(t[0].timestamp) || 0);
		const s = Cn(t), o = Qt(t, n, s);
		if (o.length === 0) return null;
		const i = An(t, o, n);
		return {
			events: o,
			turns: i,
			metadata: Yt(t, o, i, e)
		};
	}
	function Ln(t) {
		const e = bn(t);
		return jn(e.records, e.malformedLines);
	}
	const z = {
		parseTimestamp: $,
		buildNormalizedEvents: Qt,
		buildMetadata: Yt,
		getReasoningEffort: X
	}, Zt = 4e3;
	function j(t) {
		return !!(t && typeof t == "object" && !Array.isArray(t));
	}
	function te(t) {
		try {
			return JSON.parse(t.trim());
		} catch {
			return null;
		}
	}
	function ee(t) {
		if (Array.isArray(t)) return t.filter(j);
		if (j(t)) {
			if (Array.isArray(t.prompts)) return t.prompts.filter(j);
			if (Array.isArray(t.calls)) return t.calls.filter(j);
		}
		return null;
	}
	function ne(t) {
		return j(t.request) && Array.isArray(t.request.messages) && j(t.response) && j(t.response.usage);
	}
	function Wn(t) {
		const e = ee(te(t));
		return !e || e.length === 0 ? !1 : e.some(ne);
	}
	function pt(...t) {
		for (let e = 0; e < t.length; e += 1) {
			const n = t[e];
			if (typeof n == "number" && Number.isFinite(n)) return Math.max(0, n);
			if (typeof n == "string" && n.trim() !== "") {
				const s = Number(n);
				if (Number.isFinite(s)) return Math.max(0, s);
			}
		}
		return 0;
	}
	function Pn(t) {
		if (typeof t == "string") return t;
		if (Array.isArray(t)) return t.map(function(e) {
			return typeof e == "string" ? e : j(e) ? typeof e.text == "string" ? e.text : typeof e.content == "string" ? e.content : typeof e.value == "string" ? e.value : typeof e.name == "string" ? e.name : "" : "";
		}).filter(Boolean).join(`
`);
		if (j(t)) {
			if (typeof t.text == "string") return t.text;
			if (typeof t.content == "string") return t.content;
			if (typeof t.value == "string") return t.value;
		}
		return "";
	}
	function Un(t) {
		if (t.length <= Zt) return t;
		const e = Math.max(Zt - 1, 0);
		return t.slice(0, e) + "…";
	}
	function se(t) {
		return t ? Math.max(1, Math.ceil(t.length / 4)) : 0;
	}
	function qn(t) {
		try {
			return se(JSON.stringify(t || ""));
		} catch {
			return 0;
		}
	}
	function Fn(t) {
		return j(t) ? typeof t.name == "string" && t.name ? t.name : j(t.function) && typeof t.function.name == "string" && t.function.name ? t.function.name : typeof t.id == "string" && t.id ? t.id : typeof t.type == "string" && t.type ? t.type : "tool" : "tool";
	}
	function zn(t) {
		return Array.isArray(t.tools) ? t.tools : Array.isArray(t.tool_definitions) ? t.tool_definitions : Array.isArray(t.toolDefinitions) ? t.toolDefinitions : [];
	}
	function Bn(t) {
		const e = t.request || {}, n = t.response || {}, s = e.model || e.modelId || n.model || t.model || t.modelId;
		return typeof s == "string" && s.trim() ? s : null;
	}
	function vt(t) {
		return j(t) && typeof t.role == "string" ? t.role : "unknown";
	}
	function oe(t) {
		return j(t) ? Pn(t.content || t.text || t.value) : "";
	}
	function Jn(t) {
		for (let e = t.length - 1; e >= 0; e -= 1) {
			const n = t[e];
			if (vt(n) !== "user") continue;
			const s = oe(n).trim();
			if (s) return s;
		}
		return "LLM call";
	}
	function Hn(t) {
		const e = j(t.prompt_tokens_details) ? t.prompt_tokens_details : {}, n = j(t.input_tokens_details) ? t.input_tokens_details : {}, s = j(t.output_tokens_details) ? t.output_tokens_details : {}, o = pt(t.input_tokens, t.prompt_tokens, t.inputTokens, t.promptTokens, t.total_input_tokens), i = pt(t.output_tokens, t.completion_tokens, t.outputTokens, t.completionTokens, t.total_output_tokens), a = pt(t.cache_read_input_tokens, t.cache_read_tokens, t.cached_input_tokens, t.cacheRead, e.cached_tokens, n.cached_tokens), r = pt(t.cache_creation_input_tokens, t.cache_write_input_tokens, t.cache_write_tokens, t.cacheWrite, n.cache_creation_tokens, s.cache_creation_tokens);
		return o + i + a + r === 0 ? null : {
			inputTokens: o,
			outputTokens: i,
			cacheRead: a,
			cacheWrite: r,
			cacheHitRate: V(o, r, a)
		};
	}
	function Vn(t, e) {
		const n = {
			system: 0,
			tools: qn(e),
			history: 0,
			toolResults: 0,
			user: 0,
			total: 0
		}, s = t.reduce(function(o, i, a) {
			return vt(i) === "user" ? a : o;
		}, -1);
		for (let o = 0; o < t.length; o += 1) {
			const i = vt(t[o]), a = se(oe(t[o]));
			i === "system" || i === "developer" ? n.system += a : i === "tool" ? n.toolResults += a : i === "user" && o === s ? n.user += a : n.history += a;
		}
		return n.total = n.system + n.tools + n.history + n.toolResults + n.user, n;
	}
	function $n(t, e) {
		if (!e.request || !e.response || !e.response.usage) return null;
		const n = Array.isArray(e.request.messages) ? e.request.messages : [], s = zn(e.request), o = Hn(e.response.usage), i = Bn(e), a = Jn(n), r = Vn(n, s);
		return {
			t,
			agent: "user",
			track: "output",
			text: Un(a),
			duration: 1,
			intensity: o ? Math.min(1, Math.max(.25, ((o.inputTokens || 0) + (o.outputTokens || 0)) / 1e5)) : .4,
			raw: {
				copilotPrompt: e,
				costPrompt: {
					index: t,
					messages: n,
					tools: s,
					toolNames: s.map(Fn),
					contextBreakdown: r
				}
			},
			turnIndex: t,
			isError: !1,
			model: i,
			tokenUsage: o
		};
	}
	function Xn(t, e) {
		const n = {}, s = {};
		let o = 0, i = 0, a = 0, r = 0;
		for (let _ = 0; _ < t.length; _ += 1) {
			const c = t[_], x = c.model || "unknown";
			if (n[x] = (n[x] || 0) + 1, !c.tokenUsage) continue;
			const d = c.tokenUsage;
			o += d.inputTokens || 0, i += d.outputTokens || 0, a += d.cacheRead || 0, r += d.cacheWrite || 0, s[x] || (s[x] = {
				inputTokens: 0,
				outputTokens: 0,
				cacheRead: 0,
				cacheWrite: 0
			}), s[x].inputTokens += d.inputTokens || 0, s[x].outputTokens += d.outputTokens || 0, s[x].cacheRead += d.cacheRead || 0, s[x].cacheWrite += d.cacheWrite || 0;
		}
		Object.keys(s).forEach(function(_) {
			const c = s[_];
			c.cacheHitRate = V(c.inputTokens, c.cacheWrite, c.cacheRead);
		});
		const l = Object.entries(n).sort(function(_, c) {
			return c[1] - _[1];
		}), g = o + i + a + r > 0 ? {
			inputTokens: o,
			outputTokens: i,
			cacheRead: a,
			cacheWrite: r,
			cacheHitRate: V(o, r, a)
		} : null;
		return {
			totalEvents: t.length,
			totalTurns: t.length,
			totalToolCalls: 0,
			errorCount: 0,
			duration: t.length > 0 ? t.length : 0,
			models: n,
			primaryModel: l.length > 0 ? l[0][0] : null,
			tokenUsage: g,
			modelTokenUsage: s,
			totalCost: _n(s),
			format: "copilot-prompts",
			customTitle: "Copilot prompt cost analysis",
			promptCallCount: e.length
		};
	}
	function Kn(t) {
		const e = ee(te(t));
		if (!e || e.length === 0) return null;
		const n = e.filter(ne);
		if (n.length === 0) return null;
		const s = [];
		for (let o = 0; o < n.length; o += 1) {
			const i = $n(o, n[o]);
			i && s.push(i);
		}
		return s.length === 0 ? null : {
			events: s,
			turns: s.map(function(o, i) {
				return {
					index: i,
					startTime: o.t,
					endTime: o.t + o.duration,
					eventIndices: [i],
					userMessage: o.text,
					toolCount: 0,
					hasError: !1
				};
			}),
			metadata: Xn(s, n)
		};
	}
	function G(t) {
		return t ? typeof t == "string" ? t : Array.isArray(t) ? t.map(function(e) {
			return typeof e == "string" ? e : !e || typeof e != "object" ? "" : e.type === "text" ? typeof e.text == "string" ? e.text : "" : e.type === "tool_use" ? "[tool: " + (e.name || "unknown") + "]" : e.type === "tool_result" ? "[result]" : "";
		}).filter(Boolean).join(" ") : typeof t == "object" && t !== null && typeof t.text == "string" ? t.text : JSON.stringify(t).substring(0, 200) : "";
	}
	function Et(t) {
		if (!t) return "";
		if (typeof t == "string") return M(t, 100);
		if (typeof t != "object") return M(String(t), 100);
		const e = Object.keys(t);
		if (e.length === 0) return "";
		const n = e[0], s = t[n], o = typeof s == "string" ? s : JSON.stringify(s), i = e.length > 1 ? ", +" + (e.length - 1) + " more" : "";
		return M(n + ": " + o + i, 120);
	}
	function Gn(t) {
		if (!t || t.length > 600) return !1;
		const e = t.toLowerCase();
		return [
			"i'll ",
			"i need to",
			"let me",
			"first,",
			"the approach",
			"i should",
			"plan:",
			"step 1",
			"thinking about",
			"considering",
			"my strategy",
			"i want to"
		].some(function(n) {
			return e.includes(n);
		});
	}
	function Mt(t) {
		const e = t.timestamp || t.ts || t.created_at || t.createdAt;
		if (!e) return null;
		const n = new Date(e);
		return Number.isNaN(n.getTime()) ? null : n.getTime() / 1e3;
	}
	function Qn(t) {
		if (typeof t.model == "string") return t.model;
		const e = t.message && typeof t.message == "object" ? t.message : null;
		return e && typeof e.model == "string" ? e.model : null;
	}
	function St(t) {
		const e = t.usage || t.message && t.message.usage || null;
		if (!e || typeof e != "object") return null;
		const n = e.input_tokens || e.prompt_tokens || 0, s = e.output_tokens || e.completion_tokens || 0, o = e.cache_read_input_tokens || e.cache_read_tokens || 0, i = e.cache_creation_input_tokens || e.cache_write_tokens || 0, a = n + o + i;
		return a + s + o + i === 0 ? null : {
			inputTokens: a,
			outputTokens: s,
			cacheRead: o,
			cacheWrite: i
		};
	}
	function ie() {
		return {
			malformedLines: 0,
			invalidEvents: 0
		};
	}
	function re(t) {
		const e = [];
		return t && (t.malformedLines > 0 && e.push(t.malformedLines + " malformed line" + (t.malformedLines !== 1 ? "s were" : " was") + " skipped"), t.invalidEvents > 0 && e.push(t.invalidEvents + " invalid derived event" + (t.invalidEvents !== 1 ? "s were" : " was") + " skipped")), e;
	}
	function Yn(t) {
		return !!(t && typeof t.t == "number" && !Number.isNaN(t.t) && typeof t.agent == "string" && typeof t.track == "string" && typeof t.text == "string" && typeof t.duration == "number" && !Number.isNaN(t.duration) && typeof t.intensity == "number" && !Number.isNaN(t.intensity) && typeof t.isError == "boolean");
	}
	const Zn = [
		/\berror\b/i,
		/\bfailed\b/i,
		/\bexception\b/i,
		/\btraceback\b/i,
		/\bpanic\b/i,
		/\bfatal\b/i,
		/exit code [1-9]/,
		/exit status [1-9]/,
		/command not found/,
		/permission denied/i,
		/no such file/i,
		/cannot find/i
	];
	function Nt(t, e) {
		return t.is_error === !0 || t.error ? !0 : e ? Zn.some(function(n) {
			return n.test(e);
		}) : !1;
	}
	function wt(t) {
		const e = t.message && typeof t.message == "object" ? t.message : null;
		return (t.type === "assistant" || t.role === "assistant") && e && typeof e.id == "string" ? "assistant:" + e.id : null;
	}
	function ae(t, e) {
		return t ? {
			inputTokens: Math.max(t.inputTokens || 0, e.inputTokens || 0),
			outputTokens: Math.max(t.outputTokens || 0, e.outputTokens || 0),
			cacheRead: Math.max(t.cacheRead || 0, e.cacheRead || 0),
			cacheWrite: Math.max(t.cacheWrite || 0, e.cacheWrite || 0)
		} : {
			inputTokens: e.inputTokens || 0,
			outputTokens: e.outputTokens || 0,
			cacheRead: e.cacheRead || 0,
			cacheWrite: e.cacheWrite || 0
		};
	}
	function ts(t) {
		const e = /* @__PURE__ */ new Map();
		for (let n = 0; n < t.length; n += 1) {
			const s = t[n], o = St(s), i = wt(s);
			!o || !i || e.set(i, ae(e.get(i), o));
		}
		return e;
	}
	function le(t, e, n, s, o) {
		const i = [], a = Mt(t), r = a !== null ? a : e, l = Qn(t), g = wt(t), _ = g && s ? s.get(g) || null : St(t), c = !!_ && (!g || !o || !o.has(g));
		let x = !1;
		function d(u) {
			if (!Yn(u)) {
				n.invalidEvents += 1;
				return;
			}
			l && !u.model && (u.model = l), c && _ && !u.tokenUsage && !x && (u.tokenUsage = _, x = !0, g && o && o.add(g)), i.push(u);
		}
		if (t.type === "human" || t.type === "user") {
			const u = G(t.message && t.message.content != null ? t.message.content : t.message || t.content || t);
			u && d({
				t: r,
				agent: "user",
				track: "output",
				text: M(u, 300),
				duration: 1,
				intensity: .6,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "assistant" || t.role === "assistant") {
			const u = (t.message && typeof t.message == "object" ? t.message : t).content || t.content;
			if (Array.isArray(u)) {
				let p = 0;
				for (let f = 0; f < u.length; f += 1) {
					const h = u[f];
					if (h.type === "text" && h.text && (d({
						t: r + p,
						agent: "assistant",
						track: Gn(h.text) ? "reasoning" : "output",
						text: M(h.text, 300),
						duration: Math.max(1, Math.ceil(h.text.length / 500)),
						intensity: .7,
						raw: h,
						isError: !1
					}), p += .2), h.type === "tool_use" && (d({
						t: r + p,
						agent: "assistant",
						track: "tool_call",
						text: String(h.name || "tool") + "(" + Et(h.input) + ")",
						toolName: typeof h.name == "string" ? h.name : void 0,
						toolInput: h.input,
						duration: 2,
						intensity: .9,
						raw: h,
						isError: !1
					}), p += .3), h.type === "tool_result") {
						const y = G(h.content || h.output), k = Nt(h, y);
						d({
							t: r + p,
							agent: "assistant",
							track: "context",
							text: "Result: " + M(y, 200),
							duration: 1,
							intensity: k ? 1 : .5,
							raw: h,
							isError: k
						}), p += .2;
					}
					if (h.type === "thinking" || h.type === "reasoning") {
						const y = typeof h.thinking == "string" ? h.thinking : typeof h.text == "string" ? h.text : typeof h.content == "string" ? h.content : "";
						d({
							t: r + p,
							agent: "assistant",
							track: "reasoning",
							text: M(y, 300),
							duration: 2,
							intensity: .8,
							raw: h,
							isError: !1
						}), p += .2;
					}
				}
			} else typeof u == "string" && u.length > 0 && d({
				t: r,
				agent: "assistant",
				track: "output",
				text: M(u, 300),
				duration: 2,
				intensity: .7,
				raw: t,
				isError: !1
			});
		}
		if (t.role === "user" && !t.type) {
			const u = G(t.content);
			u && d({
				t: r,
				agent: "user",
				track: "output",
				text: M(u, 300),
				duration: 1,
				intensity: .6,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "tool_use") {
			const u = t.name || t.tool_name || "unknown_tool";
			d({
				t: r,
				agent: "assistant",
				track: "tool_call",
				text: String(u) + "(" + Et(t.input || t.parameters || {}) + ")",
				toolName: String(u),
				toolInput: t.input || t.parameters,
				duration: 2,
				intensity: .9,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "tool_result") {
			const u = G(t.content || t.output), p = Nt(t, u);
			d({
				t: r,
				agent: "assistant",
				track: "context",
				text: "Result: " + M(u, 200),
				duration: 1,
				intensity: p ? 1 : .5,
				raw: t,
				isError: p
			});
		}
		if (t.type === "system" || t.type === "summary") {
			const u = G(t.message || t.content || t.summary);
			u && d({
				t: r,
				agent: "system",
				track: "context",
				text: M(u, 200),
				duration: 1,
				intensity: .4,
				raw: t,
				isError: !1
			});
		}
		const m = t.data && typeof t.data == "object" ? t.data : null;
		if (t.type === "user.message" && m) {
			const u = typeof m.content == "string" ? m.content : G(m.content);
			u && d({
				t: r,
				agent: "user",
				track: "output",
				text: M(u, 300),
				duration: 1,
				intensity: .6,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "tool.execution_start" && m) {
			const u = typeof m.toolName == "string" ? m.toolName : "unknown_tool";
			let p = m.arguments;
			if (typeof p == "string") try {
				p = JSON.parse(p);
			} catch {}
			d({
				t: r,
				agent: "assistant",
				track: "tool_call",
				text: u + "(" + Et(p || {}) + ")",
				toolName: u,
				toolInput: p,
				duration: 2,
				intensity: .9,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "tool.execution_complete" && m) {
			const u = typeof m.result == "string" ? m.result : G(m.result), p = !(m.success === "True" || m.success === !0) || Nt(m, u);
			d({
				t: r,
				agent: "assistant",
				track: "context",
				text: "Result: " + M(u, 200),
				duration: 1,
				intensity: p ? 1 : .5,
				raw: t,
				isError: p
			});
		}
		if (t.type === "assistant.usage" && m) {
			const u = m.inputTokens || 0, p = m.outputTokens || 0, f = m.cacheReadTokens || 0, h = m.cacheWriteTokens || 0, y = typeof m.model == "string" ? m.model : void 0;
			u + p > 0 && d({
				t: r,
				agent: "system",
				track: "context",
				text: "Usage: " + u + " in / " + p + " out" + (y ? " (" + y + ")" : ""),
				duration: .1,
				intensity: .3,
				raw: t,
				isError: !1,
				model: y,
				tokenUsage: {
					inputTokens: u,
					outputTokens: p,
					cacheRead: f,
					cacheWrite: h
				}
			});
		}
		if (t.type === "assistant.message" && m) {
			const u = typeof m.content == "string" ? m.content : G(m.content);
			u && d({
				t: r,
				agent: "assistant",
				track: "output",
				text: M(u, 300),
				duration: 2,
				intensity: .7,
				raw: t,
				isError: !1
			});
		}
		if (t.type === "runner.error" && m) {
			const u = typeof m.message == "string" ? m.message : G(m.message);
			u && d({
				t: r,
				agent: "system",
				track: "output",
				text: M(u, 300),
				duration: 1,
				intensity: 1,
				raw: t,
				isError: !0
			});
		}
		return t.type === "skill.invoked" && m && d({
			t: r,
			agent: "assistant",
			track: "context",
			text: "Skill invoked: " + (typeof m.name == "string" ? m.name : "unknown"),
			duration: .5,
			intensity: .5,
			raw: t,
			isError: !1
		}), i;
	}
	function es(t) {
		for (let e = 0; e < t.length; e += 1) if (e < t.length - 1) {
			const n = t[e + 1].t - t[e].t;
			n >= .1 && n < 300 && (t[e].duration = n);
		}
	}
	function ns(t) {
		const e = [];
		let n = null;
		for (let s = 0; s < t.length; s += 1) {
			const o = t[s];
			o.agent === "user" ? (n && e.push(n), n = {
				index: e.length,
				startTime: o.t,
				endTime: o.t + o.duration,
				eventIndices: [s],
				userMessage: o.text,
				toolCount: 0,
				hasError: o.isError || !1
			}) : n ? (n.eventIndices.push(s), n.endTime = o.t + o.duration, o.track === "tool_call" && (n.toolCount = (n.toolCount || 0) + 1), o.isError && (n.hasError = !0)) : n = {
				index: 0,
				startTime: o.t,
				endTime: o.t + o.duration,
				eventIndices: [s],
				userMessage: "(system)",
				toolCount: o.track === "tool_call" ? 1 : 0,
				hasError: o.isError || !1
			}, o.turnIndex = n.index;
		}
		return n && e.push(n), e;
	}
	function ss(t, e, n) {
		const s = {};
		let o = 0, i = 0, a = 0, r = 0, l = 0, g = 0;
		for (let d = 0; d < t.length; d += 1) {
			const m = t[d];
			m.model && (s[m.model] = (s[m.model] || 0) + 1), m.tokenUsage && (o += m.tokenUsage.inputTokens || 0, i += m.tokenUsage.outputTokens || 0, a += m.tokenUsage.cacheRead || 0, r += m.tokenUsage.cacheWrite || 0), m.isError && (l += 1), m.track === "tool_call" && (g += 1);
		}
		const _ = et(t), c = Object.entries(s).sort(function(d, m) {
			return m[1] - d[1];
		}), x = V(o, r, a);
		return {
			totalEvents: t.length,
			totalTurns: e.length,
			totalToolCalls: g,
			errorCount: l,
			duration: _,
			models: s,
			primaryModel: c.length > 0 ? c[0][0] : null,
			tokenUsage: o + i + a + r > 0 ? {
				inputTokens: o,
				outputTokens: i,
				cacheRead: a,
				cacheWrite: r,
				cacheHitRate: x
			} : null,
			warnings: re(n),
			parseIssues: n,
			format: "claude-code"
		};
	}
	function os(t, e) {
		const n = e || ie();
		if (t.length === 0) return null;
		let s = 0;
		for (let d = 0; d < t.length; d += 1) Mt(t[d]) !== null && (s += 1);
		const o = s > t.length * .5, i = [], a = ts(t), r = /* @__PURE__ */ new Set();
		let l = 0;
		for (let d = 0; d < t.length; d += 1) {
			const m = le(t[d], l, n, a, r);
			i.push(...m), l += Math.max(1, m.length);
		}
		if (i.length === 0) return null;
		let g = Infinity;
		if (o) for (let d = 0; d < i.length; d += 1) i[d].t > 1e9 && i[d].t < g && (g = i[d].t);
		if (g === Infinity) {
			g = i[0].t;
			for (let d = 1; d < i.length; d += 1) i[d].t < g && (g = i[d].t);
		}
		for (let d = 0; d < i.length; d += 1) i[d].t = Math.max(0, i[d].t - g);
		o && es(i);
		const _ = ns(i), c = ss(i, _, n), x = t.find((d) => typeof d.sessionId == "string" && d.sessionId);
		return x && (c.sessionId = x.sessionId), {
			events: i,
			turns: _,
			metadata: c
		};
	}
	function is(t) {
		const e = t.trim().split(`
`).filter((o) => o.trim().length > 0), n = [], s = ie();
		for (let o = 0; o < e.length; o += 1) try {
			n.push(JSON.parse(e[o]));
		} catch {
			s.malformedLines += 1;
		}
		return os(n, s);
	}
	const Z = {
		extractTimestamp: Mt,
		extractUsage: St,
		getUsageDedupKey: wt,
		mergeTokenUsage: ae,
		extractEventsFromRecord: le,
		buildWarnings: re
	}, rs = 4e3, as = new Set([
		"__proto__",
		"constructor",
		"prototype"
	]);
	function Rt(t) {
		return (typeof t == "object" || typeof t == "function") && t !== null;
	}
	function ue(t, e) {
		const n = e.k;
		if (!Array.isArray(n) || n.length === 0 || n.some(function(s) {
			return as.has(String(s));
		})) return !1;
		if (e.kind === 1) {
			let s = t;
			for (let o = 0; o < n.length - 1; o += 1) {
				if (!Rt(s)) return !1;
				s = s[n[o]];
			}
			return Rt(s) ? (s[n[n.length - 1]] = e.v, !0) : !1;
		}
		if (e.kind === 2) {
			let s = t;
			for (let o = 0; o < n.length; o += 1) {
				if (!Rt(s)) return !1;
				s = s[n[o]];
			}
			if (!Array.isArray(s) || !Array.isArray(e.v)) return !1;
			for (let o = 0; o < e.v.length; o += 1) s.push(e.v[o]);
			return !0;
		}
		return !1;
	}
	function ls(t) {
		const e = t.split(`
`).filter(function(s) {
			return s.trim().length > 0;
		});
		if (e.length === 0) return null;
		let n = null;
		try {
			const s = JSON.parse(e[0]);
			if (s && typeof s == "object" && s.kind === 0 && s.v) n = s.v;
			else return null;
		} catch {
			return null;
		}
		for (let s = 1; s < e.length; s++) try {
			n && ue(n, JSON.parse(e[s]));
		} catch {}
		return n;
	}
	function it(t) {
		if (!t || typeof t != "object") return !1;
		const e = t;
		return typeof e.version == "number" && Array.isArray(e.requests) && typeof e.sessionId == "string";
	}
	function us(t) {
		const e = t.trim();
		if (!e.startsWith("{")) return !1;
		try {
			const s = JSON.parse(e);
			if (it(s) || s && s.kind === 0 && it(s.v)) return !0;
		} catch {}
		const n = e.split(`
`, 1)[0].trim();
		if (n !== e) try {
			const s = JSON.parse(n);
			if (s && s.kind === 0 && it(s.v)) return !0;
		} catch {}
		return !1;
	}
	function cs(t) {
		return t ? t.startsWith("copilot_") ? t.substring(8) : t : "unknown_tool";
	}
	function At(t) {
		return t ? typeof t.fsPath == "string" ? t.fsPath : typeof t.path == "string" ? t.path : typeof t.external == "string" ? t.external : "" : "";
	}
	const ce = [
		/\berror\b/i,
		/\bfailed\b/i,
		/\bexception\b/i,
		/\btraceback\b/i,
		/\bpanic\b/i,
		/\bfatal\b/i,
		/exit code [1-9]/,
		/command not found/,
		/permission denied/i
	];
	function fs(t) {
		if (t.toolSpecificData && t.toolSpecificData.kind === "terminal") {
			const n = t.toolSpecificData.terminalCommandState;
			if (n && n.exitCode !== 0 && n.exitCode != null) return !0;
		}
		if (t.isConfirmed && t.isConfirmed.type === 2) return !0;
		const e = t.pastTenseMessage && t.pastTenseMessage.value;
		if (e && ce.some(function(n) {
			return n.test(e);
		})) return !0;
		if (Array.isArray(t.resultDetails)) for (let n = 0; n < t.resultDetails.length; n++) {
			const s = t.resultDetails[n] && t.resultDetails[n].value;
			if (s && ce.some(function(o) {
				return o.test(s);
			})) return !0;
		}
		return !1;
	}
	function K(t, e, n, s, o, i, a, r) {
		const l = {
			t,
			agent: e,
			track: n,
			text: M(s, rs),
			duration: o,
			intensity: i,
			raw: a,
			turnIndex: 0,
			isError: !1
		};
		return r && Object.assign(l, r), l;
	}
	function fe(t, e, n) {
		const s = t.kind;
		if (s === "undoStop" || s === "prepareToolInvocation") return null;
		if (s === "thinking") {
			const o = t.value || "";
			return o.trim() ? K(e, "assistant", "reasoning", o, .5, .6, t, { model: n }) : null;
		}
		if (s === "toolInvocationSerialized") {
			const o = cs(t.toolId || ""), i = t.invocationMessage && t.invocationMessage.value || o, a = fs(t);
			let r = i;
			if (t.toolSpecificData && t.toolSpecificData.kind === "terminal") {
				const c = t.toolSpecificData.commandLine && t.toolSpecificData.commandLine.original;
				c && (r = c);
			}
			let l = 1;
			if (t.toolSpecificData && t.toolSpecificData.terminalCommandState) {
				const c = t.toolSpecificData.terminalCommandState.duration;
				c && c > 0 && (l = c / 1e3);
			}
			let g, _;
			if (t.toolSpecificData && t.toolSpecificData.kind === "terminal") g = { command: t.toolSpecificData.commandLine && t.toolSpecificData.commandLine.original }, _ = t.toolSpecificData.terminalCommandOutput && t.toolSpecificData.terminalCommandOutput.text;
			else if (t.resultDetails && t.resultDetails.length > 0) {
				const c = t.resultDetails.map(function(x) {
					return x && x.value;
				}).filter(Boolean).join(`
`);
				c && (_ = c);
			}
			return K(e, "assistant", "tool_call", r, l, .9, t, {
				toolName: o,
				toolInput: g,
				toolOutput: _ || null,
				toolCallId: t.toolCallId || null,
				isError: a,
				model: n
			});
		}
		if (s === "textEditGroup") {
			const o = At(t.uri), i = o.split(/[/\\]/).pop() || "file", a = Array.isArray(t.edits) ? t.edits.filter(function(r) {
				return Array.isArray(r) && r.length > 0;
			}).length : 0;
			return K(e, "assistant", "tool_call", "Edit " + i + " (" + a + " change" + (a !== 1 ? "s" : "") + ")", .5, .8, t, {
				toolName: "file_edit",
				toolInput: {
					file: o,
					editCount: a
				},
				model: n
			});
		}
		if (s === "codeblockUri") return K(e, "assistant", "context", "File: " + At(t.uri || t), .1, .3, t, { model: n });
		if (s === "inlineReference") return K(e, "assistant", "context", t.name || At(t.inlineReference) || "reference", .1, .3, t, { model: n });
		if (s === "elicitationSerialized") return K(e, "system", "context", (t.title || "") + (t.message ? ": " + t.message : "") || "User confirmation", .2, .4, t);
		if (s === "progressTaskSerialized") return K(e, "system", "context", t.content && t.content.value || "Progress", .1, .3, t);
		if (s === "mcpServersStarting") return K(e, "system", "context", "MCP servers starting: " + (Array.isArray(t.didStartServerIds) ? t.didStartServerIds.join(", ") : ""), .2, .3, t);
		if (!s && typeof t.value == "string") {
			const o = t.value.trim();
			return o ? K(e, "assistant", "output", o, .3, .5, t, { model: n }) : null;
		}
		return null;
	}
	function de(t, e) {
		const n = t.requests || [];
		if (n.length === 0) return {
			events: [],
			turns: []
		};
		const s = t.creationDate || n[0].timestamp || 0, o = s / 1e3, i = [], a = [];
		for (let r = 0; r < n.length; r++) {
			const l = n[r], g = (l.timestamp || s) / 1e3 - o, _ = l.result && l.result.timings && l.result.timings.totalElapsed || 0, c = l.result && l.result.timings && l.result.timings.firstProgress || 0, x = l.modelId || t.selectedModel && t.selectedModel.identifier || null, d = i.length, m = l.message && l.message.text || "";
			m && i.push(K(g, "user", "output", m, .5, .9, l, { turnIndex: r }));
			const u = l.response || [], p = u.filter(function(N) {
				return N.kind !== "undoStop" && N.kind !== "prepareToolInvocation";
			}), f = [];
			for (let N = 0; N < u.length; N++) {
				const L = u[N];
				if (L.kind === "toolInvocationSerialized" && L.toolSpecificData && L.toolSpecificData.kind === "terminal") {
					const U = L.toolSpecificData.terminalCommandState;
					U && U.timestamp && f.push({
						index: N,
						timestampSec: U.timestamp / 1e3 - o,
						durationSec: (U.duration || 0) / 1e3
					});
				}
			}
			const h = _ / 1e3 || 10;
			let y = 0;
			for (let N = 0; N < u.length; N++) {
				const L = u[N];
				if (L.kind === "undoStop" || L.kind === "prepareToolInvocation") continue;
				let U;
				const w = f.find(function(E) {
					return E.index === N;
				});
				w ? U = w.timestampSec : L.kind === "thinking" && y === 0 && c > 0 ? U = g + c / 1e3 : U = g + (p.length > 1 ? y / (p.length - 1) : 0) * h;
				const C = i[i.length - 1] || e;
				C && U <= C.t && (U = C.t + .1);
				const T = fe(L, U, x);
				T && (T.turnIndex = r, i.push(T)), y++;
			}
			const k = i.length, b = g + h;
			let I = 0, v = !1;
			const P = [];
			for (let N = d; N < k; N++) P.push(N), i[N].track === "tool_call" && I++, i[N].isError && (v = !0);
			a.push({
				index: r,
				startTime: g,
				endTime: b,
				eventIndices: P,
				userMessage: m || void 0,
				toolCount: I,
				hasError: v
			});
		}
		return {
			events: i,
			turns: a
		};
	}
	function he(t, e, n) {
		const s = {};
		let o = 0, i = 0;
		for (let l = 0; l < t.length; l++) {
			const g = t[l];
			g.model && (s[g.model] = (s[g.model] || 0) + 1), g.isError && o++, g.track === "tool_call" && i++;
		}
		const a = et(t), r = Object.entries(s).sort(function(l, g) {
			return g[1] - l[1];
		});
		return {
			totalEvents: t.length,
			totalTurns: e.length,
			totalToolCalls: i,
			errorCount: o,
			duration: a,
			models: s,
			primaryModel: r.length > 0 ? r[0][0] : null,
			tokenUsage: null,
			format: "vscode-chat",
			sessionId: typeof n.sessionId == "string" ? n.sessionId : void 0,
			customTitle: n.customTitle || void 0,
			sessionMode: n.mode && n.mode.id || void 0
		};
	}
	function ds(t) {
		if (!it(t)) return null;
		const { events: e, turns: n } = de(t);
		return e.length === 0 ? null : {
			events: e,
			turns: n,
			metadata: he(e, n, t)
		};
	}
	function hs(t) {
		let e = null;
		try {
			const n = JSON.parse(t.trim());
			n && n.kind === 0 && n.v ? e = n.v : e = n;
		} catch {
			e = ls(t);
		}
		return e ? ds(e) : null;
	}
	const rt = {
		buildTimeline: de,
		buildMetadata: he,
		isVSCodeSession: it,
		mapResponsePart: fe
	};
	function pe(t) {
		return je(t) ? "atif" : Ye(t) ? "codex" : Dn(t) ? "copilot-cli" : us(t) ? "vscode-chat" : Wn(t) ? "copilot-prompts" : "claude-code";
	}
	function me(t) {
		const e = pe(t);
		let n;
		return e === "atif" ? n = Je(t) : e === "codex" ? n = dn(t) : e === "copilot-cli" ? n = Ln(t) : e === "vscode-chat" ? n = hs(t) : e === "copilot-prompts" ? n = Kn(t) : n = is(t), n && ps(n), n;
	}
	function ps(t) {
		const e = t.events;
		if (!e || e.length === 0) return;
		const n = /* @__PURE__ */ new Map();
		for (let s = 0; s < e.length; s += 1) {
			const o = e[s];
			if (o.track !== "context") continue;
			const i = o.raw, a = i && i.payload && typeof i.payload == "object" ? i.payload : null, r = i ? [
				o.toolCallId,
				i.tool_use_id,
				i.toolCallId,
				i.tool_call_id,
				i.source_call_id,
				i.call_id,
				a && a.call_id
			] : [o.toolCallId];
			for (let l = 0; l < r.length; l += 1) {
				const g = r[l];
				if (typeof g == "string" && g.length > 0 && typeof o.text == "string" && o.text.length > 0) {
					n.set(g, o.text);
					break;
				}
			}
		}
		if (n.size !== 0) for (let s = 0; s < e.length; s += 1) {
			const o = e[s];
			if (o.track !== "tool_call" || o.toolOutput) continue;
			const i = o.raw, a = [o.toolCallId];
			i && a.push(i.id, i.toolCallId, i.tool_call_id);
			for (let r = 0; r < a.length; r += 1) {
				const l = a[r];
				if (typeof l != "string") continue;
				const g = n.get(l);
				if (g) {
					o.toolOutput = g;
					break;
				}
			}
		}
	}
	var tt = class {
		constructor() {
			this.nodes = /* @__PURE__ */ new Map();
		}
		set(t, e) {
			let n = 4294967296 + t;
			for (e === -Infinity ? this.nodes.delete(n) : this.nodes.set(n, e); n > 1;) {
				n = Math.floor(n / 2);
				const s = Math.max(this.nodes.get(n * 2) ?? -Infinity, this.nodes.get(n * 2 + 1) ?? -Infinity);
				s === -Infinity ? this.nodes.delete(n) : this.nodes.set(n, s);
			}
		}
		get max() {
			return this.nodes.get(1) ?? -Infinity;
		}
	}, at = class {
		constructor() {
			this.events = [], this.work = {
				records: 0,
				events: 0,
				turns: 0
			}, this.ends = new tt(), this.modelPositions = /* @__PURE__ */ new Map(), this.toolCount = 0, this.errorCount = 0, this.usage = {
				inputTokens: 0,
				outputTokens: 0,
				cacheRead: 0,
				cacheWrite: 0
			};
		}
		begin(t) {
			this.work = {
				records: t,
				events: 0,
				turns: 0
			};
		}
		account(t, e, n) {
			if (t.model) {
				let s = this.modelPositions.get(t.model);
				s || (s = {
					count: 0,
					positions: new tt()
				}, this.modelPositions.set(t.model, s)), s.count += n, s.positions.set(e, n > 0 ? -e : -Infinity);
			}
			t.track === "tool_call" && (this.toolCount += n), t.isError && (this.errorCount += n);
			for (const s of [
				"inputTokens",
				"outputTokens",
				"cacheRead",
				"cacheWrite"
			]) this.usage[s] += n * (t.tokenUsage?.[s] || 0);
		}
		set(t, e) {
			this.work.events++;
			const n = this.events[t];
			n && this.account(n, t, -1), this.events[t] = e, this.account(e, t, 1), this.ends.set(t, e.t + e.duration);
		}
		truncate(t) {
			for (let e = t; e < this.events.length; e++) this.work.events++, this.account(this.events[e], e, -1), this.ends.set(e, -Infinity);
			this.events.length = t;
		}
		get duration() {
			return Math.max(0, this.ends.max);
		}
		get models() {
			return Object.fromEntries([...this.modelPositions.entries()].filter(([, t]) => t.count > 0).sort((t, e) => e[1].positions.max - t[1].positions.max).map(([t, e]) => [t, e.count]));
		}
		summary(t) {
			const e = this.models;
			return {
				totalEvents: this.events.length,
				totalTurns: t.length,
				totalToolCalls: this.toolCount,
				errorCount: this.errorCount,
				duration: this.duration,
				models: e,
				primaryModel: Object.keys(e).sort((n, s) => e[s] - e[n])[0] || null
			};
		}
	};
	function F(t, e, n) {
		let s = 0, o = t.length;
		for (; s < o;) {
			const i = s + o >>> 1;
			n(t[i]) < e ? s = i + 1 : o = i;
		}
		return s;
	}
	function lt(t, e, n) {
		let s = 0, o = t.length;
		for (; s < o;) {
			const i = s + o >>> 1;
			n(t[i]) <= e ? s = i + 1 : o = i;
		}
		return s;
	}
	function Ot(t, e, n, s) {
		const o = t.events;
		let i = n > 0 ? e[o[n - 1].turnIndex] : void 0;
		i ? e.length = i.index + 1 : e.length = 0;
		for (let a = n; a < o.length; a++) {
			const r = o[a];
			t.work.turns++, (!i || r.agent === "user") && (i = {
				index: e.length,
				startTime: r.t,
				endTime: r.t + r.duration,
				eventIndices: [],
				userMessage: r.agent === "user" ? r.text : "(system)",
				toolCount: 0,
				hasError: !1
			}, e.push(i)), r.turnIndex = i.index, i.eventIndices.push(a), i.endTime = s ? Math.max(i.endTime, r.t + r.duration) : r.t + r.duration, r.track === "tool_call" && (i.toolCount = (i.toolCount || 0) + 1), r.isError && (i.hasError = !0);
		}
	}
	function ms(t) {
		const e = t.raw;
		return (e ? [
			t.toolCallId,
			e.tool_use_id,
			e.toolCallId,
			e.tool_call_id,
			e.source_call_id,
			e.call_id,
			e.payload?.call_id
		] : [t.toolCallId]).find((n) => typeof n == "string" && n.length > 0);
	}
	var ge = class {
		constructor(t) {
			this.index = t, this.results = /* @__PURE__ */ new Map(), this.resultKeys = /* @__PURE__ */ new Map(), this.calls = /* @__PURE__ */ new Map(), this.candidates = /* @__PURE__ */ new Map();
		}
		remove(t) {
			const e = this.candidates.get(t);
			if (e) {
				for (const s of e.ids) this.calls.get(s)?.delete(t);
				this.candidates.delete(t);
			}
			const n = this.resultKeys.get(t);
			if (n) {
				const s = this.results.get(n);
				s.positions.set(t, -Infinity), s.text.delete(t), this.resultKeys.delete(t);
				for (const o of this.calls.get(n) || []) this.pair(o);
			}
		}
		add(t) {
			const e = this.index.events[t];
			if (this.index.work.events++, e.track === "context" && e.text) {
				const n = ms(e);
				if (n) {
					this.results.has(n) || this.results.set(n, {
						positions: new tt(),
						text: /* @__PURE__ */ new Map()
					});
					const s = this.results.get(n);
					s.positions.set(t, t), s.text.set(t, e.text), this.resultKeys.set(t, n);
					for (const o of this.calls.get(n) || []) this.pair(o);
				}
			}
			if (e.track === "tool_call" && (!e.toolOutput || this.candidates.has(t))) {
				const n = e.raw, s = [
					e.toolCallId,
					n?.id,
					n?.toolCallId,
					n?.tool_call_id
				].filter((o) => typeof o == "string");
				this.candidates.set(t, {
					ids: s,
					original: this.candidates.get(t)?.original || e
				});
				for (const o of s) this.calls.has(o) || this.calls.set(o, /* @__PURE__ */ new Set()), this.calls.get(o).add(t);
				this.pair(t);
			}
		}
		pair(t) {
			const e = this.candidates.get(t);
			if (!e) return;
			let n = e.original.toolOutput;
			for (const o of e.ids) {
				const i = this.results.get(o);
				if (i && i.positions.max !== -Infinity && (n = i.text.get(i.positions.max), n)) break;
			}
			const s = this.index.events[t];
			if (s.toolOutput !== n) {
				const o = {
					...s,
					toolOutput: n
				};
				n === void 0 && !("toolOutput" in e.original) && delete o.toolOutput, this.index.set(t, o);
			}
		}
	}, gs = class {
		constructor() {
			this.index = new at(), this.pairs = new ge(this.index), this.originals = [], this.turns = [], this.recordCount = 0, this.timestampCount = 0, this.syntheticTime = 0, this.minimum = Infinity, this.realMinimum = Infinity, this.origin = Infinity, this.realTimestamps = !1, this.usage = /* @__PURE__ */ new Map(), this.firstUsageEvent = /* @__PURE__ */ new Map(), this.attached = /* @__PURE__ */ new Set(), this.issues = {
				malformedLines: 0,
				invalidEvents: 0
			};
		}
		get work() {
			return this.index.work;
		}
		append(t, e) {
			const n = this.index;
			n.begin(t.length);
			const s = this.originals.length, o = /* @__PURE__ */ new Set();
			for (const c of t) {
				this.recordCount++, Z.extractTimestamp(c) !== null && this.timestampCount++, !this.identity && typeof c.sessionId == "string" && c.sessionId && (this.identity = c.sessionId);
				const x = Z.getUsageDedupKey(c), d = Z.extractUsage(c);
				x && d && this.usage.set(x, Z.mergeTokenUsage(this.usage.get(x), d));
				const m = Z.extractEventsFromRecord(c, this.syntheticTime, this.issues, this.usage, this.attached);
				if (this.syntheticTime += Math.max(1, m.length), x && m.length && !this.firstUsageEvent.has(x) && this.firstUsageEvent.set(x, this.originals.length), x && this.usage.has(x) && this.firstUsageEvent.has(x)) {
					const u = this.firstUsageEvent.get(x), p = u < this.originals.length ? this.originals[u] : m[0];
					if (p.tokenUsage = this.usage.get(x), u < this.originals.length) {
						for (const f of m) delete f.tokenUsage;
						o.add(u);
					}
					this.attached.add(x);
				}
				for (const u of m) this.minimum = Math.min(this.minimum, u.t), u.t > 1e9 && (this.realMinimum = Math.min(this.realMinimum, u.t)), this.originals.push(u);
			}
			if (this.issues.malformedLines = e, !this.originals.length) return null;
			const i = this.timestampCount > this.recordCount * .5, a = i && this.realMinimum !== Infinity ? this.realMinimum : this.minimum, r = a !== this.origin || i !== this.realTimestamps;
			this.origin = a, this.realTimestamps = i;
			const l = r ? 0 : Math.max(0, s - 1);
			for (let c = l; c < this.originals.length; c++) {
				const x = this.originals[c], d = {
					...x,
					t: Math.max(0, x.t - a)
				};
				if (i && c + 1 < this.originals.length) {
					const m = Math.max(0, this.originals[c + 1].t - a) - d.t;
					m >= .1 && m < 300 && (d.duration = m);
				}
				c < s && (d.turnIndex = n.events[c]?.turnIndex), n.set(c, d);
			}
			for (const c of o) c < l && n.set(c, {
				...n.events[c],
				tokenUsage: this.originals[c].tokenUsage
			});
			if (r) this.turns = [], Ot(n, this.turns, 0, !1);
			else {
				if (s > 0) {
					const c = n.events[s - 1];
					this.turns[c.turnIndex].endTime = c.t + c.duration, n.work.turns++;
				}
				Ot(n, this.turns, s, !1);
			}
			for (let c = l; c < n.events.length; c++) this.pairs.add(c);
			const g = n.usage, _ = {
				...n.summary(this.turns),
				tokenUsage: g.inputTokens + g.outputTokens + g.cacheRead + g.cacheWrite > 0 ? {
					...g,
					cacheHitRate: V(g.inputTokens, g.cacheWrite, g.cacheRead)
				} : null,
				warnings: Z.buildWarnings(this.issues),
				parseIssues: { ...this.issues },
				format: "claude-code",
				...this.identity ? { sessionId: this.identity } : {}
			};
			return {
				events: n.events,
				turns: this.turns,
				metadata: _
			};
		}
	}, ys = class Me {
		constructor() {
			this.index = new at(), this.nodes = [], this.slots = [], this.dependencies = /* @__PURE__ */ new Map(), this.completes = Object.create(null), this.retained = {
				taskToolMap: Object.create(null),
				subagentStartTimes: Object.create(null),
				subagentLifecycle: Object.create(null)
			}, this.seenStarts = /* @__PURE__ */ new Set(), this.start = null, this.foundStart = !1, this.firstTimestamp = 0, this.info = {}, this.turns = [], this.turnValues = [], this.boundaries = [], this.lastUser = null, this.effortChanges = [], this.effortHistory = /* @__PURE__ */ new Set(), this.currentEffort = null, this.hasInitialEffort = !1;
		}
		get work() {
			return this.index.work;
		}
		depend(e, n) {
			typeof e == "string" && (this.dependencies.has(e) || this.dependencies.set(e, /* @__PURE__ */ new Set()), this.dependencies.get(e).add(n));
		}
		effort(e) {
			return this.effortChanges[lt(this.effortChanges, e, (n) => n.t) - 1]?.effort;
		}
		owner(e) {
			return this.boundaries[lt(this.boundaries, e, (n) => n.t) - 1]?.winner ?? (this.turns.length ? 0 : -1);
		}
		remove(e, n) {
			const s = this.turns[n.turnIndex];
			if (!s || !s.value.eventIndices.length) return;
			const o = F(s.value.eventIndices, e, (i) => i);
			s.value.eventIndices[o] === e && (s.value.eventIndices.splice(o, 1), s.ends.set(e, -Infinity), n.track === "tool_call" && s.value.toolCount--, n.isError && s.errors--, s.value.hasError = s.errors > 0, s.value.endTime = Math.max(s.end, s.ends.max), this.index.work.turns++);
		}
		assign(e) {
			const n = this.index.events[e], s = this.owner(n.t);
			if (n.turnIndex = Math.max(0, s), s < 0) return;
			const o = this.turns[s], i = F(o.value.eventIndices, e, (a) => a);
			o.value.eventIndices.splice(i, 0, e), o.ends.set(e, n.t + n.duration), n.track === "tool_call" && o.value.toolCount++, n.isError && o.errors++, o.value.hasError = o.errors > 0, o.value.endTime = Math.max(o.end, o.ends.max), this.index.work.turns++;
		}
		replace(e, n) {
			const s = this.index.events[e], o = this.owner(n.t), i = this.turns[o]?.value.eventIndices, a = i && i[F(i, e, (r) => r)] === e;
			if (o >= 0 && s.turnIndex === o && a) {
				const r = this.turns[o];
				r.ends.set(e, n.t + n.duration), r.value.toolCount += +(n.track === "tool_call") - +(s.track === "tool_call"), r.errors += Number(n.isError) - Number(s.isError), r.value.hasError = r.errors > 0, r.value.endTime = Math.max(r.end, r.ends.max), n.turnIndex = o, this.index.set(e, n), this.index.work.turns++;
			} else o < 0 ? this.index.set(e, n) : (this.remove(e, s), this.index.set(e, n), this.assign(e));
		}
		append(e, n) {
			const s = this.index;
			s.begin(e.length);
			const o = /* @__PURE__ */ new Set(), i = [];
			let a = Infinity, r = Infinity, l = !1;
			const g = (u) => {
				for (const p of this.dependencies.get(String(u)) || []) o.add(p);
			};
			for (const u of e) {
				const p = u.data || {};
				this.nodes.length || (this.firstTimestamp = z.parseTimestamp(u.timestamp) || 0);
				const f = u.type === "session.start" ? p.startTime : u.type === "session.resume" ? p.resumeTime : null;
				if (!this.foundStart && f) {
					const y = z.parseTimestamp(f) ?? this.firstTimestamp;
					l ||= this.start !== null && y !== this.start, this.start = y, this.foundStart = !0;
				}
				this.start ??= this.firstTimestamp, [
					"session.start",
					"session.resume",
					"session.shutdown"
				].includes(u.type) && (this.info[u.type] = u);
				const h = {
					record: u,
					positions: [],
					ordinal: this.nodes.length
				};
				if (this.nodes.push(h), i.push(h), u.type === "tool.execution_complete" && p.toolCallId && (this.completes[p.toolCallId] = u, g(p.toolCallId)), u.type === "tool.execution_start" && p.toolName === "task") {
					const y = p.arguments || {};
					this.retained.taskToolMap[p.toolCallId] = {
						agentType: y.agent_type || "task",
						description: y.description || y.name || ""
					}, g(p.toolCallId);
				}
				if (u.type === "subagent.started") {
					const y = z.parseTimestamp(u.timestamp);
					p.toolCallId && y !== null && (this.retained.subagentStartTimes[p.toolCallId] = y), this.retained.subagentLifecycle[p.toolCallId || ""] = {
						agentName: p.agentName || p.agentType,
						agentDisplayName: p.agentDisplayName || p.agentName
					}, g(p.toolCallId || "");
				}
				this.depend(p.toolCallId, h), this.depend(p.parentToolCallId, h);
				for (const y of Array.isArray(p.toolRequests) ? p.toolRequests : []) this.depend(y.toolCallId, h);
				if (u.type === "user.message" && (this.lastUser = p.content || ""), u.type === "assistant.turn_start") {
					const y = z.parseTimestamp(u.timestamp), k = y ? y - this.start : 0, b = this.turns.length;
					this.turns.push({
						value: {
							index: b,
							startTime: k,
							endTime: 0,
							eventIndices: [],
							userMessage: this.lastUser || "(continuation)",
							toolCount: 0,
							hasError: !1
						},
						ends: new tt(),
						errors: 0,
						end: 0
					}), this.turnValues.push(this.turns[b].value), this.lastUser = null;
					const I = F(this.boundaries, k, (v) => v.t);
					this.boundaries.splice(I, 0, {
						t: k,
						ordinal: b,
						winner: b
					});
					for (let v = I; v < this.boundaries.length; v++) this.boundaries[v].winner = Math.max(this.boundaries[v].ordinal, this.boundaries[v - 1]?.winner ?? -1), s.work.turns++;
					r = Math.min(r, b === 0 ? -Infinity : k);
				}
				if (u.type === "assistant.turn_end" && this.turns.length) {
					const y = z.parseTimestamp(u.timestamp), k = this.turns[this.turns.length - 1];
					y && (k.end = y - this.start), k.value.endTime = Math.max(k.end, k.ends.max), s.work.turns++;
				}
				if ([
					"session.start",
					"session.resume",
					"session.model_change"
				].includes(u.type)) {
					const y = z.getReasoningEffort(p), k = u.type === "session.model_change" ? z.getReasoningEffort(p, "previousReasoningEffort") : null;
					k && this.effortHistory.add(k), y && this.effortHistory.add(y), this.currentEffort = y || this.currentEffort;
					const b = [];
					!this.hasInitialEffort && k && (b.push({
						t: 0,
						effort: k
					}), this.hasInitialEffort = !0);
					const I = z.parseTimestamp(u.type === "session.start" ? p.startTime || u.timestamp : u.type === "session.resume" && p.resumeTime || u.timestamp);
					y && I !== null && (b.push({
						t: Math.max(I - this.start, 0),
						effort: y
					}), u.type !== "session.model_change" && (this.hasInitialEffort = !0));
					for (const v of b) {
						const P = lt(this.effortChanges, v.t, (N) => N.t);
						this.effortChanges.splice(P, 0, v), a = Math.min(a, v.t);
					}
				}
			}
			if (l) return this.rebuild(n);
			const _ = [];
			for (const u of i) {
				const p = u.record.data || {};
				if (u.record.type === "tool.execution_start" && typeof p.toolCallId == "string" && p.toolCallId) {
					if (this.seenStarts.has(p.toolCallId) && z.parseTimestamp(u.record.timestamp) !== null) continue;
					z.parseTimestamp(u.record.timestamp) !== null && this.seenStarts.add(p.toolCallId);
				}
				z.buildNormalizedEvents([u.record], this.start, { completes: this.completes }, this.retained).forEach((f, h) => _.push({
					node: u,
					offset: h,
					event: f
				})), o.delete(u);
			}
			for (const u of o) {
				if (!u.positions.length) continue;
				s.work.records++;
				const p = z.buildNormalizedEvents([u.record], this.start, { completes: this.completes }, this.retained);
				u.positions.forEach((f, h) => {
					const y = p[h];
					delete y.reasoningEffort;
					const k = this.effort(y.t);
					k && (y.reasoningEffort = k), this.slots[f].event = y, this.replace(f, y);
				});
			}
			_.sort((u, p) => u.event.t - p.event.t || u.node.ordinal - p.node.ordinal || u.offset - p.offset);
			let c = s.events.length;
			for (const u of _) {
				let p = 0, f = this.slots.length;
				for (; p < f;) {
					const h = p + f >>> 1, y = this.slots[h];
					y.event.t < u.event.t || y.event.t === u.event.t && y.node.ordinal <= u.node.ordinal ? p = h + 1 : f = h;
				}
				c = Math.min(c, p), this.slots.splice(p, 0, u);
			}
			for (let u = s.events.length - 1; u >= c; u--) this.remove(u, s.events[u]);
			for (let u = c; u < this.slots.length; u++) {
				const p = this.slots[u];
				p.node.positions[p.offset] = u;
				const f = { ...p.event };
				delete f.reasoningEffort;
				const h = this.effort(f.t);
				h && (f.reasoningEffort = h), s.set(u, f), this.assign(u);
			}
			for (let u = F(s.events, Math.min(r, a), (p) => p.t); u < c; u++) if (s.events[u].t >= r && (this.remove(u, s.events[u]), this.assign(u)), s.events[u].t >= a) {
				const p = { ...s.events[u] };
				delete p.reasoningEffort;
				const f = this.effort(p.t);
				f && (p.reasoningEffort = f), s.set(u, p);
			}
			if (!s.events.length) return null;
			const x = this.turnValues, d = s.summary(x), m = z.buildMetadata(Object.values(this.info), [], [], n, d);
			return Object.assign(m, {
				totalEvents: s.events.length,
				totalTurns: x.length,
				reasoningEffort: this.currentEffort,
				reasoningEfforts: [...this.effortHistory]
			}), {
				events: s.events,
				turns: x,
				metadata: m
			};
		}
		rebuild(e) {
			const n = this.nodes.map((i) => i.record), s = new Me();
			s.start = this.start, s.foundStart = !0;
			const o = s.append(n, e);
			return Object.assign(this, s), o;
		}
	}, xs = class {
		constructor() {
			this.index = new at(), this.contexts = new at(), this.contextPositions = /* @__PURE__ */ new Map(), this.state = {
				currentTurnId: null,
				currentModel: null,
				turnContexts: {},
				turns: {},
				turnCount: 0
			}, this.slots = [], this.syntheticTime = 0, this.origin = Infinity, this.nextOrdinal = 0, this.buckets = /* @__PURE__ */ new Map(), this.boundaries = [], this.turns = [], this.owners = [], this.unresolved = [], this.byTurnId = /* @__PURE__ */ new Map(), this.webCalls = [], this.webByQuery = /* @__PURE__ */ new Map(), this.webBarrier = -1, this.pendingTurns = /* @__PURE__ */ new Set(), this.turnOrder = /* @__PURE__ */ new Map(), this.pairs = new ge(this.index);
		}
		get work() {
			return this.index.work;
		}
		target(t) {
			return (typeof t.codexTurnId == "string" ? this.buckets.get(t.codexTurnId) : void 0) || this.boundaries[Math.max(0, lt(this.boundaries, t.t, (n) => n.turn.startTime) - 1)];
		}
		refresh(t) {
			const e = this.state.turns[t.id], n = this.state.turnContexts[t.id], s = Math.max(0, e.startTime - this.origin), o = e.endTime === null ? s : Math.max(s, e.endTime - this.origin), i = t.turn;
			i.startTime = s, i.endTime = Math.max(o, t.ends.max), i.userMessage = e.userMessage || n?.summary || "(continuation)", i.userMessage === "(continuation)" && t.users.max !== -Infinity && (i.userMessage = this.index.events[-t.users.max].text), i.model = n?.model || null, i.effort = n?.effort || null, i.hasError = t.errors > 0, this.index.work.turns++;
		}
		remove(t) {
			const e = this.owners[t], n = this.index.events[t];
			if (e) {
				const o = e.turn.eventIndices, i = F(o, t, (a) => a);
				o[i] === t && o.splice(i, 1), e.ends.set(t, -Infinity), e.users.set(t, -Infinity), n.track === "tool_call" && e.turn.toolCount--, n.isError && e.errors--;
			}
			typeof n.codexTurnId == "string" && this.byTurnId.get(n.codexTurnId)?.delete(t);
			const s = F(this.unresolved, t, (o) => o);
			this.unresolved[s] === t && this.unresolved.splice(s, 1), this.owners[t] = void 0;
		}
		assign(t, e) {
			const n = this.index.events[t], s = this.target(n);
			this.owners[t] = s;
			const o = s.turn.eventIndices;
			o.splice(F(o, t, (i) => i), 0, t), s.ends.set(t, n.t + n.duration), n.agent === "user" && s.users.set(t, -t), n.track === "tool_call" && s.turn.toolCount++, n.isError && s.errors++, typeof n.codexTurnId == "string" && (this.byTurnId.has(n.codexTurnId) || this.byTurnId.set(n.codexTurnId, /* @__PURE__ */ new Set()), this.byTurnId.get(n.codexTurnId).add(t)), (typeof n.codexTurnId != "string" || !this.buckets.has(n.codexTurnId)) && this.unresolved.splice(F(this.unresolved, t, (i) => i), 0, t), e.add(s), n.turnIndex = s.turn.index, this.index.work.turns++;
		}
		append(t, e) {
			const n = this.index;
			n.begin(t.length);
			const s = [], o = this.pendingTurns, i = [], a = /* @__PURE__ */ new Set(), r = this.buckets.size > 0;
			for (const f of t) {
				const h = f.payload || {};
				!this.meta && f.type === "session_meta" && q.isRecord(f.payload) && (this.meta = f), f.type === "event_msg" && h.type === "token_count" && q.isRecord(h.info?.total_token_usage) && (this.token = f);
				const y = q.getEventTime(f, this.syntheticTime++), k = [];
				if (f.type === "turn_context") {
					if (q.updateTurnContext(f, this.state), typeof h.turn_id == "string") {
						const I = h.turn_id;
						o.add(I), this.contextPositions.has(I) || this.contextPositions.set(I, this.contextPositions.size), this.contexts.set(this.contextPositions.get(I), {
							t: 0,
							duration: 0,
							agent: "system",
							track: "context",
							text: "",
							intensity: 0,
							isError: !1,
							model: this.state.turnContexts[I].model
						}), n.work.events++;
					}
				} else if (f.type === "event_msg") {
					if (h.type === "web_search_end") {
						if (h.call_id) {
							const v = q.getWebSearchQuery(h), P = v ? this.webByQuery.get(v) || [] : this.webCalls, N = P[P.length - 1];
							N && N.ordinal > this.webBarrier && (N.event.toolCallId = h.call_id, this.webBarrier = N.ordinal, a.add(N));
						}
						q.pushToolOutputEvent(k, f, this.state, y);
					} else q.handleEventMessage(f, this.state, k, y);
					const I = typeof h.turn_id == "string" ? h.turn_id : this.state.currentTurnId;
					I && o.add(I);
				} else f.type === "response_item" && q.isRecord(f.payload) && (h.type === "message" ? q.pushMessageEvent(k, f, this.state, y) : h.type === "reasoning" ? q.pushReasoningEvent(k, f, this.state, y) : [
					"function_call",
					"custom_tool_call",
					"web_search_call"
				].includes(h.type) ? q.pushToolCallEvent(k, f, this.state, y) : ["function_call_output", "custom_tool_call_output"].includes(h.type) && q.pushToolOutputEvent(k, f, this.state, y), this.state.currentTurnId && o.add(this.state.currentTurnId));
				for (const I of k) {
					const v = {
						event: I,
						ordinal: this.nextOrdinal++,
						position: -1
					};
					if (s.push(v), I.toolName === "web_search") {
						this.webCalls.push(v);
						const P = q.getWebSearchQuery(I.raw.payload);
						this.webByQuery.has(P) || this.webByQuery.set(P, []), this.webByQuery.get(P).push(v), I.toolCallId && (this.webBarrier = v.ordinal);
					}
				}
				const b = typeof h.turn_id == "string" ? h.turn_id : this.state.currentTurnId;
				b && this.state.turns[b] && !this.turnOrder.has(b) && this.turnOrder.set(b, this.turnOrder.size);
			}
			let l = this.slots.length;
			for (const f of s) {
				let h = 0, y = this.slots.length;
				for (; h < y;) {
					const k = h + y >>> 1;
					this.slots[k].event.t <= f.event.t ? h = k + 1 : y = k;
				}
				this.slots.splice(h, 0, f), l = Math.min(l, h);
			}
			if (!this.slots.length) return null;
			const g = this.slots[0].event.t, _ = g !== this.origin;
			if (this.origin = g, _) {
				l = 0;
				for (const f of this.buckets.keys()) o.add(f);
			}
			const c = /* @__PURE__ */ new Set();
			for (const f of o) {
				if (!this.state.turns[f]) continue;
				let h = this.buckets.get(f);
				h || (h = {
					id: f,
					ordinal: this.turnOrder.get(f),
					active: !1,
					ends: new tt(),
					users: new tt(),
					errors: 0,
					turn: {
						index: 0,
						startTime: 0,
						endTime: 0,
						eventIndices: [],
						toolCount: 0,
						hasError: !1,
						turnId: f
					}
				}, this.buckets.set(f, h), i.push(h)), this.refresh(h), c.add(h);
			}
			for (const f of i) {
				let h = F(this.boundaries, f.turn.startTime, (y) => y.turn.startTime);
				for (; h < this.boundaries.length && this.boundaries[h].turn.startTime === f.turn.startTime && this.boundaries[h].ordinal < f.ordinal;) h++;
				this.boundaries.splice(h, 0, f);
			}
			_ && this.boundaries.sort((f, h) => f.turn.startTime - h.turn.startTime || f.ordinal - h.ordinal), !r && this.buckets.size && (l = 0, this.turns = []);
			const x = n.events.length;
			for (let f = x - 1; f >= l; f--) this.owners[f] && c.add(this.owners[f]), this.pairs.remove(f), this.remove(f);
			for (let f = l; f < this.slots.length; f++) {
				const h = this.slots[f];
				h.position = f, n.set(f, {
					...h.event,
					t: Math.max(0, h.event.t - g)
				}), this.buckets.size && this.assign(f, c);
			}
			if (this.buckets.size && l > 0) {
				const f = /* @__PURE__ */ new Set();
				for (const h of i) {
					for (const I of this.byTurnId.get(h.id) || []) I < l && f.add(I);
					let y = F(this.boundaries, h.turn.startTime, (I) => I.turn.startTime);
					for (; this.boundaries[y] !== h;) y++;
					const k = y === 0 ? 0 : F(n.events, h.turn.startTime, (I) => I.t), b = this.boundaries[y + 1]?.turn.startTime ?? Infinity;
					for (let I = F(this.unresolved, k, (v) => v); I < this.unresolved.length; I++) {
						const v = this.unresolved[I];
						if (v >= l || n.events[v].t >= b) break;
						f.add(v);
					}
				}
				for (const h of f) this.target(n.events[h]) !== this.owners[h] && (this.owners[h] && c.add(this.owners[h]), this.remove(h), this.assign(h, c));
			}
			if (this.buckets.size) {
				let f = this.turns.length;
				const h = [...c].filter((y) => y.active && !y.turn.eventIndices.length).sort((y, k) => k.turn.index - y.turn.index);
				for (const y of h) {
					const k = y.turn.index;
					this.turns.splice(k, 1), f = Math.min(f, k), y.active = !1;
				}
				for (const y of c) {
					this.refresh(y);
					const k = y.turn.eventIndices.length > 0;
					if (k !== y.active) {
						if (k) {
							let b = F(this.turns, y.turn.startTime, (I) => I.startTime);
							for (; b < this.turns.length && this.turns[b].startTime === y.turn.startTime && this.buckets.get(String(this.turns[b].turnId)).ordinal < y.ordinal;) b++;
							this.turns.splice(b, 0, y.turn), f = Math.min(f, b);
						}
						y.active = k;
					}
				}
				_ && (this.turns.sort((y, k) => y.startTime - k.startTime || this.buckets.get(String(y.turnId)).ordinal - this.buckets.get(String(k.turnId)).ordinal), f = 0);
				for (let y = f; y < this.turns.length; y++) {
					const k = this.turns[y];
					k.index = y, n.work.turns++;
					for (const b of k.eventIndices) n.events[b].turnIndex = y, n.work.events++;
				}
				for (let y = l; y < n.events.length; y++) n.events[y].turnIndex = this.owners[y].turn.index;
			} else {
				let f = l;
				if (l < x) {
					let h;
					for (let y = this.turns.length - 1; y >= 0; y--) if (n.work.turns++, this.turns[y].eventIndices[0] <= l) {
						h = this.turns[y];
						break;
					}
					f = h?.eventIndices[0] || 0, this.turns.length = h?.index || 0, f > 0 && (n.events[f - 1].turnIndex = this.turns.length - 1);
				}
				Ot(n, this.turns, f, !0);
			}
			for (const f of a) f.position < l && n.set(f.position, {
				...n.events[f.position],
				toolCallId: f.event.toolCallId
			});
			const d = l;
			for (let f = d; f < n.events.length; f++) this.pairs.add(f);
			for (const f of a) f.position < d && this.pairs.add(f.position);
			const m = this.contexts.models;
			for (const [f, h] of Object.entries(n.models)) m[f] = (m[f] || 0) + h;
			const u = [this.meta, this.token].filter((f) => !!f), p = q.buildMetadata(u, [], [], {
				...this.state,
				turnContexts: {}
			}, e, [], {
				...n.summary(this.turns),
				models: m
			});
			return p.totalEvents = n.events.length, p.totalTurns = this.turns.length, o.clear(), {
				events: n.events,
				turns: this.turns,
				metadata: p
			};
		}
	}, Ts = class {
		constructor() {
			this.index = new at(), this.session = null, this.turns = [], this.offsets = [], this.parts = [], this.errors = [];
		}
		get work() {
			return this.index.work;
		}
		append(t) {
			const e = this.index;
			e.begin(t.length);
			let n = Infinity, s = -1;
			const o = this.session?.creationDate || this.session?.requests?.[0]?.timestamp || 0, i = /* @__PURE__ */ new Set();
			let a = !1;
			const r = /* @__PURE__ */ new Map();
			for (const c of t) {
				if (!this.session && (c.kind === 0 || rt.isVSCodeSession(c))) {
					this.session = structuredClone(c.kind === 0 ? c.v : c), n = 0, a = !0, s = (this.session?.requests?.length || 0) - 1;
					continue;
				}
				if (!this.session) continue;
				const x = this.session.requests?.length || 0;
				if (!ue(this.session, c)) continue;
				const d = c.k;
				if (d[0] === "requests") {
					if (d.length === 2 && d[1] === "length") {
						n = Math.min(n, this.session.requests.length), s = Math.max(s, this.session.requests.length - 1);
						continue;
					}
					const m = d.length > 1 ? Number(d[1]) : c.kind === 2 ? x : 0;
					if (d[2] === "response" && d.length >= 4 && Number.isInteger(Number(d[3]))) {
						r.has(m) || r.set(m, /* @__PURE__ */ new Set()), r.get(m).add(Number(d[3]));
						continue;
					}
					if (n = Math.min(n, Number.isInteger(m) && m >= 0 ? m : 0), s = Math.max(s, d.length > 1 ? m : (this.session.requests?.length || 0) - 1), d.length > 1 && Number.isInteger(m)) i.add(m);
					else if (d.length === 1 && c.kind === 2) for (let u = x; u < this.session.requests.length; u++) i.add(u);
					else a = !0;
				} else (d[0] === "creationDate" || d[0] === "selectedModel") && (n = 0, a = !0, s = (this.session.requests?.length || 0) - 1);
			}
			if (!this.session || !rt.isVSCodeSession(this.session)) return null;
			const l = this.session.requests;
			for (const [c, x] of r) for (const d of x) {
				e.work.records++;
				const m = this.parts[c]?.get(d), u = l[c]?.response?.[d], p = u?.toolSpecificData?.terminalCommandState?.timestamp, f = m && e.events[m.position], h = f && u && u.kind === m.kind && p === m.timestamp ? rt.mapResponsePart(u, f.t, f.model || null) : null;
				if (!h || !m || !f) {
					n = Math.min(n, c), s = Math.max(s, c), i.add(c);
					continue;
				}
				this.errors[c] += Number(h.isError) - Number(f.isError), this.turns[c].toolCount += +(h.track === "tool_call") - +(f.track === "tool_call"), this.turns[c].hasError = this.errors[c] > 0, e.set(m.position, {
					...h,
					turnIndex: c
				}), e.work.turns++;
			}
			(this.session.creationDate || l[0]?.timestamp || 0) !== o && (n = 0, s = l.length - 1, a = !0);
			const g = [...i].sort((c, x) => c - x);
			for (let c = n; c < l.length; c++) {
				e.work.records++;
				const x = this.offsets[c] ?? e.events.length, d = this.offsets[c + 1] ?? e.events.length, m = e.events[d - 1]?.t, u = d - x, p = {
					...this.session,
					creationDate: this.session.creationDate || l[0]?.timestamp || 0,
					requests: [l[c]]
				}, f = rt.buildTimeline(p, e.events[x - 1]);
				u !== f.events.length && (e.truncate(x), this.offsets.length = c + 1, this.turns.length = c, s = l.length - 1, a = !0), this.offsets[c] = x, f.events.forEach((b, I) => e.set(x + I, {
					...b,
					turnIndex: c
				}));
				const h = new Map(f.events.map((b, I) => [b.raw, x + I]));
				this.parts[c] = /* @__PURE__ */ new Map();
				const y = l[c].response || [];
				for (let b = 0; b < y.length; b++) {
					const I = y[b], v = h.get(I);
					v !== void 0 && this.parts[c].set(b, {
						position: v,
						kind: I.kind,
						timestamp: I.toolSpecificData?.terminalCommandState?.timestamp
					}), e.work.events++;
				}
				this.errors[c] = f.events.reduce((b, I) => b + Number(I.isError), 0), this.offsets[c + 1] = x + f.events.length;
				const k = f.turns[0];
				if (k.index = c, k.eventIndices = k.eventIndices.map((b) => b + x), this.turns[c] = k, e.work.turns++, m === e.events[this.offsets[c + 1] - 1]?.t) {
					if (c >= s) break;
					if (!a) {
						const b = g[lt(g, c, (I) => I)];
						if (b === void 0) break;
						c = b - 1;
					}
				}
			}
			if (this.turns.length > l.length && (e.truncate(this.offsets[l.length] || 0), this.turns.length = l.length, this.offsets.length = l.length + 1, this.parts.length = l.length, this.errors.length = l.length), !e.events.length) return null;
			const _ = {
				...rt.buildMetadata([], [], this.session),
				...e.summary(this.turns)
			};
			return {
				events: e.events,
				turns: this.turns,
				metadata: _
			};
		}
	};
	function ks(t = !0) {
		return {
			rawText: "",
			pendingText: "",
			completeLineCount: 0,
			parsedRecordCount: 0,
			malformedLineCount: 0,
			lastAppendParsedLineCount: 0,
			format: null,
			result: null,
			records: [],
			normalizer: null,
			normalizationWork: {
				records: 0,
				events: 0,
				turns: 0
			},
			snapshot: t,
			initialFullParseCount: 0,
			fallbackFullParseCount: 0
		};
	}
	function _s(t, e) {
		return t ? e ? t.endsWith(`
`) || e.startsWith(`
`) ? t + e : t + `
` + e : t : e;
	}
	function ye(t) {
		if (!t) return {
			lines: [],
			pendingText: ""
		};
		const e = t.split(`
`), n = t.endsWith(`
`) || t.endsWith("\r"), s = [];
		let o = "";
		for (let i = 0; i < e.length; i += 1) {
			const a = e[i], r = a.trim(), l = i === e.length - 1;
			if (r) if (l && !n) try {
				JSON.parse(r), s.push(r);
			} catch {
				o = a;
			}
			else s.push(r);
		}
		return {
			lines: s,
			pendingText: o
		};
	}
	function xe(t) {
		const e = [];
		let n = 0;
		for (let s = 0; s < t.length; s += 1) try {
			const o = JSON.parse(t[s]);
			o && typeof o == "object" ? e.push(o) : n += 1;
		} catch {
			n += 1;
		}
		return {
			records: e,
			malformedLines: n
		};
	}
	function Is(t) {
		return (t.type === "session.start" || t.type === "session.resume") && t.data && (t.data.producer === "copilot-agent" || t.data.copilotVersion);
	}
	function bs(t) {
		const e = t.v;
		return !!(t.kind === 0 && e && typeof e.version == "number" && typeof e.sessionId == "string" && Array.isArray(e.requests));
	}
	function Te(t) {
		return t.length === 0 ? null : Is(t[0]) ? "copilot-cli" : bs(t[0]) ? "vscode-chat" : Ft(t) ? "codex" : null;
	}
	function Dt(t) {
		return Te(t) || (t.length > 0 ? "claude-code" : null);
	}
	function ke(t) {
		return t === "codex" ? new xs() : t === "copilot-cli" ? new ys() : t === "vscode-chat" ? new Ts() : t === "claude-code" ? new gs() : null;
	}
	function jt(t) {
		try {
			const e = JSON.parse(t.trim());
			return !!(e && typeof e == "object" && typeof e.version == "number" && typeof e.sessionId == "string" && Array.isArray(e.requests));
		} catch {
			return !1;
		}
	}
	function _e(t, e, n, s = !0) {
		const o = ye(t), i = xe(o.lines), a = jt(t);
		a && (i.records = [JSON.parse(t)]);
		const r = a ? "vscode-chat" : Dt(i.records.slice(0, 8)) || (t.trim() ? pe(t) : null), l = ke(r), g = l?.append(i.records, i.malformedLines) || (t.trim() ? me(t) : null);
		return {
			rawText: t,
			pendingText: o.pendingText,
			completeLineCount: o.lines.length,
			parsedRecordCount: i.records.length,
			malformedLineCount: i.malformedLines,
			lastAppendParsedLineCount: 0,
			format: r,
			result: s && g ? structuredClone(g) : g,
			records: i.records,
			normalizer: l,
			normalizationWork: l?.work || {
				records: 0,
				events: 0,
				turns: 0
			},
			snapshot: s,
			initialFullParseCount: e,
			fallbackFullParseCount: n
		};
	}
	function Lt(t, e = {}) {
		if (!t.trim()) return ks(e.snapshot);
		const n = _e(t, jt(t) ? 1 : 0, 0, e.snapshot);
		return !n.result && jt(t) ? {
			...n,
			result: me(t),
			format: "vscode-chat"
		} : n;
	}
	function Cs(t, e) {
		const n = t.pendingText ? t.rawText + e : _s(t.rawText, e), s = ye(t.pendingText + e), o = xe(s.lines), i = Te(o.records), a = Dt(t.records.slice(0, 8).concat(o.records.slice(0, Math.max(0, 8 - t.records.length))));
		if (t.format && (i && i !== t.format || a && a !== t.format)) {
			const d = _e(n, t.initialFullParseCount, t.fallbackFullParseCount + 1, t.snapshot);
			return {
				state: d,
				result: d.result
			};
		}
		const r = t.format || a || i || Dt(o.records), l = t.records;
		for (const d of o.records) l.push(d);
		const g = t.malformedLineCount + o.malformedLines, _ = t.normalizer || ke(r), c = _?.append(o.records, g) || null, x = t.snapshot && c ? structuredClone(c) : c;
		return {
			state: {
				rawText: n,
				pendingText: s.pendingText,
				completeLineCount: t.completeLineCount + s.lines.length,
				parsedRecordCount: l.length,
				malformedLineCount: g,
				lastAppendParsedLineCount: s.lines.length,
				format: r,
				result: x,
				records: l,
				normalizer: _,
				normalizationWork: _?.work || {
					records: 0,
					events: 0,
					turns: 0
				},
				snapshot: t.snapshot,
				initialFullParseCount: t.initialFullParseCount,
				fallbackFullParseCount: t.fallbackFullParseCount
			},
			result: x
		};
	}
	let Y = Lt("", { snapshot: !1 });
	self.onmessage = ({ data: t }) => {
		try {
			t.initial !== void 0 && (Y = Lt(t.initial, { snapshot: !1 })), t.reset && (Y = Lt("", { snapshot: !1 })), t.text && (Y = Cs(Y, t.text).state), self.postMessage({
				result: Y.result,
				rawText: Y.rawText,
				normalizationWork: Y.normalizationWork
			});
		} catch (e) {
			self.postMessage({ error: e instanceof Error ? e.message : String(e) });
		}
	};
})();
